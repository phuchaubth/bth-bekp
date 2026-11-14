using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Yêu cầu tra cứu: mã người bệnh <b>hoặc</b> số CCCD (đúng một trong hai), gửi trong thân để không lộ trong URL/nhật ký.</summary>
public sealed record PatientLookupRequest(string? PatientCode, string? CitizenId);

/// <summary>
/// API thông tin người bệnh: dịch vụ riêng, chỉ đọc, là nơi duy nhất kết nối CSDL bệnh viện. Các phần mềm khác (phiếu đồng
/// thuận…) gọi qua HTTPS với khóa API, không cần giữ chuỗi kết nối CSDL.
/// </summary>
public static partial class PatientInfoProgram
{
    public static void Main(string[] args)
    {
        var app = Build(WebApplication.CreateBuilder(args));
        app.Run();
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<PatientInfoOptions>()
            .Bind(builder.Configuration.GetSection(PatientInfoOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Thiếu PatientInfo__ConnectionString (chuỗi kết nối CSDL bệnh viện, tài khoản chỉ đọc).")
            .Validate(o => o.HasValidQuery, "PatientInfo:Query phải là một câu SELECT (hoặc WITH … SELECT) dùng tham số @PatientCode và/hoặc @CitizenId, không có dấu ';' giữa câu.")
            .Validate(o => o.HasValidApiKeys, $"Thiếu khóa API: đặt PatientInfo__ApiKeys__0 (mỗi khóa ≥ {PatientInfoOptions.MinimumApiKeyLength} ký tự ngẫu nhiên).")
            .Validate(o => o.QueryTimeoutSeconds is >= 1 and <= 60, "PatientInfo:QueryTimeoutSeconds phải trong khoảng 1–60 giây.")
            .ValidateOnStart();
        builder.Services.AddSingleton<PatientRepository>();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
        app.MapPost("/api/v1/patients/lookup", LookupAsync);
        return app;
    }

    internal static async Task<IResult> LookupAsync(
        PatientLookupRequest? request,
        PatientRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var patientCode = string.IsNullOrWhiteSpace(request?.PatientCode) ? null : request.PatientCode.Trim();
        var citizenId = string.IsNullOrWhiteSpace(request?.CitizenId) ? null : request.CitizenId.Trim();
        if ((patientCode is null) == (citizenId is null))
        {
            return Problem("Nhập mã người bệnh hoặc số CCCD (một trong hai).");
        }

        if (patientCode is { Length: > 50 })
        {
            return Problem("Mã người bệnh tối đa 50 ký tự.");
        }

        if (citizenId is not null && !CitizenIdPattern().IsMatch(citizenId))
        {
            return Problem("Số CCCD gồm 12 chữ số.");
        }

        var logger = loggerFactory.CreateLogger("PatientInfo.Lookup");
        try
        {
            var patient = await repository.FindAsync(patientCode, citizenId, cancellationToken).ConfigureAwait(false);

            // Nhật ký không ghi mã người bệnh/CCCD hay thông tin trả về.
            logger.LogInformation("Tra cứu người bệnh theo {By}: {Result}", patientCode is null ? "citizenId" : "patientCode", patient is null ? "NotFound" : "Found");
            return patient is null ? Results.NotFound(new { title = "Không tìm thấy người bệnh." }) : Results.Ok(patient);
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Lỗi CSDL khi tra cứu người bệnh (mã lỗi SQL {Number}).", ex.Number);
            return Results.Problem("Không truy vấn được CSDL bệnh viện.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult Problem(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    [GeneratedRegex("^[0-9]{12}$")]
    private static partial Regex CitizenIdPattern();
}
