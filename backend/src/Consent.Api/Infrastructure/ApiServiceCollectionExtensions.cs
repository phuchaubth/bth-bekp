using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Consent.Api.Authorization;
using Consent.Application.Abstractions;
using Consent.Application.Auth;
using Consent.Application.ConsentTemplates;
using Consent.Application.Patients;
using Consent.Infrastructure.Security;
using Consent.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Consent.Api.Infrastructure;

internal static class ApiServiceCollectionExtensions
{
    public const string CorsPolicyName = "Frontend";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(o => o.MaxFailedAttempts > 0 && o.LockoutMinutes > 0 && o.RefreshTokenLifetimeHours > 0,
                "Cấu hình Auth không hợp lệ: các giá trị số lần sai, thời gian khóa và thời hạn phiên phải lớn hơn 0.")
            .ValidateOnStart();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            ProblemDetailsMapping.Localize(context.ProblemDetails);
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptions<HisOptions>()
            .Bind(configuration.GetSection(HisOptions.SectionName))
            .Validate(o => o.TimeoutSeconds is > 0 and <= 60, "His:TimeoutSeconds phải trong khoảng 1–60 giây.")
            .ValidateOnStart();

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                // Enum dạng chuỗi ("Male", "Found"…) để API dễ đọc và ổn định khi thêm giá trị mới.
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                // Định nghĩa mẫu phiếu trả về gọn như bản đang lưu (không có thuộc tính null/false).
                options.JsonSerializerOptions.TypeInfoResolver =
                    (options.JsonSerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                    .WithAddedModifier(TemplateJson.OmitEmptyDefinitionMembers);
            })
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
            {
                var details = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Dữ liệu gửi lên không hợp lệ.",
                };
                details.Extensions["code"] = "validation";
                details.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                return new BadRequestObjectResult(details) { ContentTypes = { ProblemDetailsMapping.ProblemJson } };
            });
        services.AddOpenApi("v1");

        AddAuthentication(services);
        AddAuthorization(services);
        services.AddApiRateLimiting(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<ConsentDbContext>("database", tags: [ReadyTag]);

        // Chỉ cho phép các origin được cấu hình rõ ràng (mặc định: không có origin nào).
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }));

        // API chạy sau reverse proxy (nginx/IIS) chịu trách nhiệm TLS. Chỉ tin X-Forwarded-* khi được cấu hình,
        // ví dụ API không mở cổng ra ngoài và chỉ nhận yêu cầu từ reverse proxy.
        var trustAllProxies = configuration.GetValue<bool>("ReverseProxy:TrustAllProxies");
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (trustAllProxies)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        return services;
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseRouting();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        }

        app.MapControllers();

        // Liveness: tiến trình còn chạy (không kiểm tra phụ thuộc).
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponseAsync,
        }).AllowAnonymous();

        // Readiness: kiểm tra kết nối SQL Server.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthResponseAsync,
        }).AllowAnonymous();

        return app;
    }

    private static void AddAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddJwtBearer(SigningSession.AuthenticationScheme);
        // Token đăng nhập thường và token phiên ký có đối tượng nhận khác nhau: mỗi loại chỉ dùng được ở nơi của nó.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) => ConfigureBearer(bearer, jwtOptions.Value, jwtOptions.Value.Audience));
        services.AddOptions<JwtBearerOptions>(SigningSession.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
                ConfigureBearer(bearer, jwtOptions.Value, jwtOptions.Value.SigningAudience));
    }

    private static void ConfigureBearer(JwtBearerOptions bearer, JwtOptions jwt, string audience)
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.HasValidSigningKey ? jwt.CreateSigningKey() : null,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AppClaimTypes.UserName,
            RoleClaimType = AppClaimTypes.Role,
        };
    }

    private static void AddAuthorization(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddAuthorization(options =>
        {
            // Mặc định an toàn: mọi endpoint đều yêu cầu đăng nhập, trừ khi đánh dấu [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            // Chỉ token phiên ký (chế độ ký trên máy tính bảng) mới gửi được chữ ký của người bệnh.
            options.AddPolicy(SigningSession.PolicyName, new AuthorizationPolicyBuilder(SigningSession.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaimTypes.SigningForm)
                .RequireClaim(AppClaimTypes.SigningSession)
                .Build());
        });
    }

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
