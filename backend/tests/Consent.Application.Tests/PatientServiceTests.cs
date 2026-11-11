using Consent.Application.Abstractions;
using Consent.Application.Patients;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class PatientServiceTests
{
    private const string CitizenId = "001085000001";

    private static PatientRequest Request(string? citizenId = CitizenId, string? patientCode = null) => new(
        "Nguyễn Văn An (giả lập)", new DateOnly(1985, 3, 12), Gender.Male, citizenId, null, new DateOnly(2021, 8, 15),
        "Địa chỉ giả lập", null, "0900000001", "Kinh", null, "Việt Nam", null, patientCode);

    [Fact]
    public async Task Create_WhenHisFindsPatient_StoresHisPatientCode_AndMarksVerified()
    {
        var ctx = new AppTestContext();
        ctx.His.Result = StubHisGateway.Found("BN85000001");
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HisVerified);
        Assert.Equal("BN85000001", result.Value.PatientCode);
    }

    [Fact]
    public async Task Create_WhenHisIsNotConnected_SavesWithoutCallingHis_AndKeepsManualPatientCode()
    {
        var ctx = new AppTestContext();
        ctx.His.IsEnabled = false;
        ctx.His.Result = StubHisGateway.Found("BN85000001");
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(patientCode: "BN-NHAP-TAY"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HisVerified);
        Assert.Equal("BN-NHAP-TAY", result.Value.PatientCode);
        Assert.Equal(0, ctx.His.Calls);
    }

    [Fact]
    public async Task Lookup_WhenHisIsNotConnected_ReturnsDisabled_WithoutCallingHis()
    {
        var ctx = new AppTestContext();
        ctx.His.IsEnabled = false;
        var service = ctx.CreatePatientService();
        var existing = (await service.CreateAsync(Request(), CancellationToken.None)).Value;

        var result = await service.LookupAsync(new PatientLookupRequest(CitizenId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HisLookupStatus.Disabled, result.Value.HisStatus);
        Assert.Null(result.Value.HisPatient);
        Assert.Equal(existing.Id, result.Value.ExistingPatient?.Id);
        Assert.Equal(0, ctx.His.Calls);
    }

    [Fact]
    public async Task Create_WhenHisIsDown_StillSaves_ButNotVerified()
    {
        var ctx = new AppTestContext();
        ctx.His.Throw = true;
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HisVerified);
        Assert.Single(ctx.Store.Patients);
    }

    [Fact]
    public async Task Create_WhenHisTimesOut_StillSaves_ButNotVerified()
    {
        var ctx = new AppTestContext();
        ctx.His.Hang = true;
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HisVerified);
    }

    [Fact]
    public async Task Create_WithPatientCodeDifferentFromHis_IsNotMarkedVerified()
    {
        var ctx = new AppTestContext();
        ctx.His.Result = StubHisGateway.Found("BN85000001");
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(patientCode: "BN-NHAP-TAY"), CancellationToken.None);

        Assert.False(result.Value.HisVerified);
        Assert.Equal("BN-NHAP-TAY", result.Value.PatientCode);
    }

    [Fact]
    public async Task Create_WithExistingCitizenId_ReturnsConflict_WithExistingPatientId()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        var first = (await service.CreateAsync(Request(), CancellationToken.None)).Value;

        var duplicate = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal("patients.duplicate_citizen_id", duplicate.Error!.Code);
        Assert.Equal(first.Id, duplicate.Error.Metadata!["existingPatientId"]);
        Assert.Single(ctx.Store.Patients);
    }

    [Fact]
    public async Task Create_WithCitizenIdNotMatchingBirthDate_ReturnsFieldError()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request() with { DateOfBirth = new DateOnly(1990, 1, 1) }, CancellationToken.None);

        Assert.Equal(["Số CCCD không khớp với năm sinh."], result.Error!.FieldErrors!["citizenId"]);
    }

    [Theory]
    [InlineData("phoneNumber", "123")]
    [InlineData("healthInsuranceNumber", "BHYT-SAI")]
    [InlineData("oldIdNumber", "12345")]
    public async Task Create_WithInvalidOptionalFields_ReturnsFieldErrors(string field, string value)
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        var request = field switch
        {
            "phoneNumber" => Request() with { PhoneNumber = value },
            "healthInsuranceNumber" => Request() with { HealthInsuranceNumber = value },
            _ => Request() with { OldIdNumber = value },
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Contains(field, result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_WithFutureBirthDate_IsRejected()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();

        var result = await service.CreateAsync(Request(citizenId: null) with { DateOfBirth = new DateOnly(2030, 1, 1) }, CancellationToken.None);

        Assert.Contains("dateOfBirth", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Lookup_ReturnsExistingPatientAndHisInfo_AndAuditsWithoutCitizenId()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        var created = (await service.CreateAsync(Request(), CancellationToken.None)).Value;
        ctx.His.Result = StubHisGateway.Found("BN85000001");

        var result = await service.LookupAsync(new PatientLookupRequest(CitizenId), CancellationToken.None);

        Assert.Equal(created.Id, result.Value.ExistingPatient!.Id);
        Assert.Equal(HisLookupStatus.Found, result.Value.HisStatus);
        var audit = Assert.Single(ctx.Store.AuditLogs, a => a.Action == AuditActions.PatientLookedUp);
        Assert.Equal(created.Id.ToString(), audit.EntityId);
        Assert.DoesNotContain(CitizenId, audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lookup_UnknownCitizen_WithHisDown_ReportsUnavailable()
    {
        var ctx = new AppTestContext();
        ctx.His.Throw = true;
        var service = ctx.CreatePatientService();

        var result = await service.LookupAsync(new PatientLookupRequest("079192000002"), CancellationToken.None);

        Assert.Null(result.Value.ExistingPatient);
        Assert.Equal(HisLookupStatus.Unavailable, result.Value.HisStatus);
    }

    [Fact]
    public async Task Lookup_WithInvalidCitizenId_ReturnsValidationError_WithoutCallingHis()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();

        var result = await service.LookupAsync(new PatientLookupRequest("12345"), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, ctx.His.Calls);
    }

    [Fact]
    public async Task Search_MasksCitizenId()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        await service.CreateAsync(Request(), CancellationToken.None);

        var page = await service.SearchAsync(new PatientQuery(), CancellationToken.None);

        Assert.Equal("0010******01", Assert.Single(page.Items).CitizenIdMasked);
    }

    [Fact]
    public async Task Get_WritesViewedAuditEntry()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        var created = (await service.CreateAsync(Request(), CancellationToken.None)).Value;

        var result = await service.GetAsync(created.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.PatientViewed && a.EntityId == created.Id.ToString());
    }

    [Fact]
    public async Task Representatives_AddUpdateRemove()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreatePatientService();
        var patient = (await service.CreateAsync(Request(), CancellationToken.None)).Value;

        var added = await service.AddRepresentativeAsync(patient.Id,
            new RepresentativeRequest("Nguyễn Thị Mẹ (giả lập)", RepresentativeRelationship.Mother, "001160000002", new DateOnly(1960, 2, 2), Gender.Female),
            CancellationToken.None);
        var representativeId = Assert.Single(added.Value.Representatives).Id;
        var updated = await service.UpdateRepresentativeAsync(patient.Id, representativeId,
            new RepresentativeRequest("Nguyễn Thị Mẹ (giả lập)", RepresentativeRelationship.Guardian), CancellationToken.None);
        var removed = await service.RemoveRepresentativeAsync(patient.Id, representativeId, CancellationToken.None);
        var missing = await service.RemoveRepresentativeAsync(patient.Id, representativeId, CancellationToken.None);

        Assert.Equal(RepresentativeRelationship.Guardian, updated.Value.Representatives[0].Relationship);
        Assert.Empty(removed.Value.Representatives);
        Assert.Equal("patients.representative_not_found", missing.Error!.Code);
    }
}
