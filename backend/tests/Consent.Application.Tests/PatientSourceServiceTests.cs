using Consent.Application.Abstractions;
using Consent.Application.Patients;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class PatientSourceServiceTests
{
    /// <summary>Nguồn giả lập: ghi lại tham số, trả kết quả định sẵn hoặc ném lỗi.</summary>
    private sealed class StubSource(bool enabled = true) : IPatientSource
    {
        public PatientSourceResult Result { get; set; } = PatientSourceResult.NotFound;

        public Exception? Throws { get; set; }

        public (string? Code, string? CitizenId)? LastQuery { get; private set; }

        public bool IsEnabled => enabled;

        public Task<PatientSourceResult> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken)
        {
            LastQuery = (patientCode, citizenId);
            return Throws is null ? Task.FromResult(Result) : throw Throws;
        }
    }

    private static PatientSourceService Service(AppTestContext ctx, IPatientSource source) => new(
        source,
        new InMemoryPatientRepository(ctx.Store),
        new InMemoryAuditLogRepository(ctx.Store),
        ctx.Store,
        ctx.Clock,
        ctx.CurrentUser,
        new FakeRequestContext(),
        Microsoft.Extensions.Options.Options.Create(ctx.HisOptions));

    private static ExternalPatient Sample(string citizenId = "079070000123") => new(
        "BN-GL-001", "Lê Văn Thử (giả lập)", new DateOnly(1970, 2, 3), Gender.Male, citizenId, "0900000123",
        "Số 1 Đường Giả Lập", null, "Kinh", null, "Việt Nam", "GD479079070000123");

    [Theory]
    [InlineData(null, null)]
    [InlineData("BN-GL-001", "079070000123")]
    [InlineData("  ", "")]
    public async Task ExactlyOneOfPatientCodeOrCitizenId_IsRequired(string? code, string? citizenId)
    {
        var ctx = new AppTestContext();

        var result = await Service(ctx, new StubSource()).LookupAsync(new(code, citizenId), CancellationToken.None);

        Assert.Equal(["Nhập mã người bệnh hoặc số CCCD (một trong hai)."], result.Error!.FieldErrors!["patientCode"]);
    }

    [Fact]
    public async Task InvalidCitizenId_IsRejected_WithoutCallingTheSource()
    {
        var ctx = new AppTestContext();
        var source = new StubSource();

        var result = await Service(ctx, source).LookupAsync(new(null, "12345"), CancellationToken.None);

        Assert.Contains("citizenId", result.Error!.FieldErrors!.Keys);
        Assert.Null(source.LastQuery);
    }

    [Fact]
    public async Task Found_ReturnsAdministrativeInfo_AndTheExistingRecordWithTheSameCitizenId_AuditedWithoutIdentifiers()
    {
        var ctx = new AppTestContext();
        var existing = Patient.Create(
            new PatientDetails("Lê Văn Thử (giả lập)", new DateOnly(1970, 2, 3), Gender.Male, "079070000123", null, null,
                null, null, null, null, null, "Việt Nam", null, null),
            ctx.Clock.UtcNow);
        ctx.Store.Patients.Add(existing);
        var source = new StubSource { Result = new(PatientSourceStatus.Found, Sample()) };

        var result = await Service(ctx, source).LookupAsync(new(" BN-GL-001 ", null), CancellationToken.None);

        Assert.Equal(("BN-GL-001", (string?)null), source.LastQuery);
        Assert.Equal(PatientSourceStatus.Found, result.Value.Status);
        Assert.Equal("Lê Văn Thử (giả lập)", result.Value.Patient!.FullName);
        Assert.Equal(existing.Id, result.Value.ExistingPatient!.Id);
        var audit = Assert.Single(ctx.Store.AuditLogs, a => a.Action == AuditActions.PatientSourceLookedUp);
        Assert.DoesNotContain("BN-GL-001", audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("079070000123", audit.Details!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceFailure_IsReportedAsUnavailable_SoStaffCanTypeByHand()
    {
        var ctx = new AppTestContext();
        var source = new StubSource { Throws = new InvalidOperationException("mất kết nối") };

        var result = await Service(ctx, source).LookupAsync(new(null, "079070000123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PatientSourceStatus.Unavailable, result.Value.Status);
        Assert.Null(result.Value.Patient);
    }

    [Fact]
    public async Task DisabledSource_ReturnsDisabled()
    {
        var ctx = new AppTestContext();

        var result = await Service(ctx, new StubSource(enabled: false)).LookupAsync(new("BN-GL-001", null), CancellationToken.None);

        Assert.Equal(PatientSourceStatus.Disabled, result.Value.Status);
    }
}
