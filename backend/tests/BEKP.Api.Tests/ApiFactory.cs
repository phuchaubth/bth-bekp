using System.Security.Claims;
using System.Text.Encodings.Web;
using BEKP.Application.Abstractions;
using BEKP.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BEKP.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTime Now = new(2026, 11, 17, 2, 42, 0, DateTimeKind.Utc);

    public InMemoryInpatientRepository Repository { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Mode", "Jwt");
        builder.UseSetting("Authentication:Authority", "https://login.example.test/realms/bekp");
        builder.UseSetting("Authentication:Audience", "bekp-api");
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultScheme = TestAuthHandler.SchemeName;
            });
            services.AddSingleton<IInpatientRepository>(Repository);
            services.AddSingleton<IUnitOfWork>(Repository);
            services.AddSingleton<IDashboardNotifier>(Notifier);
            services.AddSingleton<IClock>(new FakeClock(Now));
        });
    }
}

/// <summary>Authenticates when the X-Test-Roles header is present (comma separated roles).</summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-client") };
        claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r.Trim())));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
