using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BEKP.Shared.Security;
using BEKP.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BEKP.Api.Tests;

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;

    public ApiTests(ApiFactory factory)
    {
        this.factory = factory;
        if (factory.Repository.Departments.Count == 0)
        {
            var ward = new WardBuilder(factory.Repository, ApiFactory.Now);
            ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 3);
            ward.Department("DEP-0004", "Ghép tế bào gốc tạo máu", beds: 2, sterile: true);
        }
    }

    [Fact]
    public async Task Dashboard_RequiresAuthentication()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/dashboard/overview", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RealtimeHub_RequiresAuthentication()
    {
        var response = await factory.CreateClient().PostAsync(new Uri("/hubs/dashboard/negotiate?negotiateVersion=1", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RealtimeHub_NegotiatesForViewer()
    {
        var response = await Client(BekpRoles.DashboardViewer).PostAsync(new Uri("/hubs/dashboard/negotiate?negotiateVersion=1", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_RequiresViewerRole()
    {
        var response = await Client(BekpRoles.Integration).GetAsync(new Uri("/api/dashboard/overview", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overview_ReturnsCamelCaseJsonWithStringEnums()
    {
        var response = await Client(BekpRoles.DashboardViewer).GetAsync(new Uri("/api/dashboard/overview", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var departments = json.RootElement.GetProperty("departments");
        Assert.True(departments.GetArrayLength() >= 2);
        Assert.Contains(departments[0].GetProperty("occupancyLevel").GetString(), new[] { "normal", "high", "critical", "overCapacity" });
        Assert.Equal("malignantHematology", departments[0].GetProperty("group").GetString());
        Assert.True(json.RootElement.TryGetProperty("diseasePattern", out _));
    }

    [Fact]
    public async Task Department_UnknownOrMalformedCode_Returns404()
    {
        var client = Client(BekpRoles.DashboardViewer);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/api/dashboard/departments/DEP-9999", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/api/dashboard/departments/abc';--", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task TimeStandards_ValidatesDepartmentAndPeriod()
    {
        var client = Client(BekpRoles.DashboardViewer);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/dashboard/time-standards?period=last7Days&department=DEP-0001", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(new Uri("/api/dashboard/time-standards?department=x", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(new Uri("/api/dashboard/time-standards?period=forever", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Ingestion_ForbiddenForViewers()
    {
        var response = await Client(BekpRoles.DashboardViewer).PostAsJsonAsync(new Uri("/api/inpatient/stays", UriKind.Relative), ValidAdmission("ADM-100001", "100-1"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ingestion_RejectsInvalidPayload_WithProblemDetails()
    {
        var response = await Client(BekpRoles.Integration).PostAsJsonAsync(new Uri("/api/inpatient/stays", UriKind.Relative), new
        {
            departmentCode = "DEP-0001",
            bedCode = "100-1",
            stayCode = "Nguyen Van A",
            careLevel = "level3",
            principalDiagnosisCode = "C92.0",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("StayCode", out _));
    }

    [Fact]
    public async Task Ingestion_RejectsUnknownEnumValues()
    {
        var response = await Client(BekpRoles.Integration).PostAsJsonAsync(new Uri("/api/inpatient/stays", UriKind.Relative), new
        {
            departmentCode = "DEP-0001",
            bedCode = "100-2",
            stayCode = "ADM-100009",
            careLevel = 7,
            principalDiagnosisCode = "C92.0",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admission_Succeeds_ThenDuplicateBedIsConflict_AndBroadcasts()
    {
        var client = Client(BekpRoles.Integration);
        var first = await client.PostAsJsonAsync(new Uri("/api/inpatient/stays", UriKind.Relative), ValidAdmission("ADM-200001", "100-3"));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Contains(factory.Notifier.Published, e => e.StayCode == "ADM-200001");

        var second = await client.PostAsJsonAsync(new Uri("/api/inpatient/stays", UriKind.Relative), ValidAdmission("ADM-200002", "100-3"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous_AndResponsesCarrySecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task UnknownApiRoute_IsNotServedAsSpa()
    {
        var response = await Client(BekpRoles.DashboardViewer).GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpaRoutes_AreAnonymous_WithBrowserContentSecurityPolicy()
    {
        // The SPA shell carries no data; wwwroot is only populated by the IIS publish package.
        var response = await factory.CreateClient().GetAsync(new Uri("/departments/DEP-0001", UriKind.Relative));
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentAuthentication_IsRefusedOutsideDevelopment()
    {
        using var unsafeFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Authentication:Mode", "Development");
        });

        var ex = Assert.ThrowsAny<Exception>(() => unsafeFactory.CreateClient());
        Assert.Contains("only allowed in the Development environment", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Jwt_RequiresAuthorityAndAudience()
    {
        using var misconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Authentication:Mode", "Jwt");
            b.UseSetting("Authentication:Authority", "");
        });

        var ex = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
        Assert.Contains("Authority", ex.ToString(), StringComparison.Ordinal);
    }

    private static object ValidAdmission(string stayCode, string bedCode) => new
    {
        departmentCode = "DEP-0001",
        bedCode,
        stayCode,
        careLevel = "level3",
        principalDiagnosisCode = "C92.0",
    };

    private HttpClient Client(string roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }
}
