using Consent.Domain.Common;
using Consent.Domain.Patients;

namespace Consent.Domain.Tests;

public sealed class CitizenIdNumberTests
{
    [Theory]
    [InlineData("001085000001", true)]
    [InlineData("096299000123", true)]
    [InlineData("000085000001", false)] // mã tỉnh 000 không tồn tại
    [InlineData("097085000001", false)] // mã tỉnh > 096
    [InlineData("00108500000", false)]  // 11 số
    [InlineData("0010850000011", false)] // 13 số
    [InlineData("00108500000A", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasValidFormat(string? value, bool expected)
    {
        Assert.Equal(expected, CitizenIdNumber.HasValidFormat(value));
    }

    [Theory]
    [InlineData("001085000001", 1985, Gender.Male)]
    [InlineData("079192000002", 1992, Gender.Female)]
    [InlineData("048210000010", 2010, Gender.Male)]
    [InlineData("048305000011", 2005, Gender.Female)]
    [InlineData("001999000001", 1899, Gender.Female)]
    [InlineData("001899000001", 1899, Gender.Male)]
    public void DerivesBirthYearAndGender(string value, int year, Gender gender)
    {
        Assert.Equal(year, CitizenIdNumber.BirthYear(value));
        Assert.Equal(gender, CitizenIdNumber.GenderOf(value));
        Assert.Null(CitizenIdNumber.CheckConsistency(value, new DateOnly(year, 6, 1), gender));
    }

    [Fact]
    public void CheckConsistency_ReportsYearAndGenderMismatch()
    {
        Assert.Equal("Số CCCD không khớp với năm sinh.",
            CitizenIdNumber.CheckConsistency("001085000001", new DateOnly(1986, 1, 1), Gender.Male));
        Assert.Equal("Số CCCD không khớp với giới tính.",
            CitizenIdNumber.CheckConsistency("001085000001", new DateOnly(1985, 1, 1), Gender.Female));
    }
}

public sealed class SensitiveMaskTests
{
    [Fact]
    public void MasksMiddleOfValues()
    {
        Assert.Equal("0010******01", SensitiveMask.CitizenId("001085000001"));
        Assert.Equal("090*****01", SensitiveMask.PhoneNumber("0900000001"));
        Assert.Equal("DN**********002", SensitiveMask.HealthInsuranceNumber("DN4790000000002"));
        Assert.Null(SensitiveMask.CitizenId(null));
        Assert.Equal("***", SensitiveMask.Mask("abc", 2, 2));
    }

    [Fact]
    public void SensitiveProperties_AreMarkedForAuditMasking()
    {
        string[] expected = [nameof(Patient.CitizenId), nameof(Patient.OldIdNumber), nameof(Patient.PhoneNumber), nameof(Patient.HealthInsuranceNumber)];

        var marked = typeof(Patient).GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(SensitiveDataAttribute), false).Length > 0)
            .Select(p => p.Name)
            .Order();

        Assert.Equal(expected.Order(), marked);
    }
}

public sealed class PatientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private static PatientDetails Details(string? citizenId = "001085000001", string? patientCode = null) => new(
        "  Nguyễn Văn An (giả lập) ", new DateOnly(1985, 3, 12), Gender.Male, citizenId, null, new DateOnly(2021, 8, 15),
        "Địa chỉ giả lập", null, " 0900000001 ", "Kinh", null, "Việt Nam", "dn4790000000002", patientCode);

    [Fact]
    public void Create_TrimsAndNormalizesValues()
    {
        var patient = Patient.Create(Details(), Now);

        Assert.Equal("Nguyễn Văn An (giả lập)", patient.FullName);
        Assert.Equal("0900000001", patient.PhoneNumber);
        Assert.Equal("DN4790000000002", patient.HealthInsuranceNumber);
        Assert.Null(patient.HisVerifiedAtUtc);
        Assert.Equal(Now, patient.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithCitizenIdNotMatchingBirthYear_Throws()
    {
        var details = Details() with { DateOfBirth = new DateOnly(1990, 1, 1) };

        Assert.Throws<DomainException>(() => Patient.Create(details, Now));
    }

    [Fact]
    public void ChangingPatientCode_ClearsHisVerification()
    {
        var patient = Patient.Create(Details(), Now);
        patient.MarkHisVerified("BN85000001", Now);

        patient.Update(Details(patientCode: "BN85000001"), Now.AddMinutes(1));
        Assert.NotNull(patient.HisVerifiedAtUtc);

        patient.Update(Details(patientCode: "BN-KHAC"), Now.AddMinutes(2));
        Assert.Null(patient.HisVerifiedAtUtc);
    }

    [Fact]
    public void Representatives_CanBeAddedUpdatedAndRemoved()
    {
        var patient = Patient.Create(Details(), Now);
        var representative = patient.AddRepresentative(
            new RepresentativeDetails("Nguyễn Thị Mẹ (giả lập)", RepresentativeRelationship.Mother, "001160000002", new DateOnly(1960, 1, 1), Gender.Female, null, null),
            Now);

        patient.UpdateRepresentative(representative.Id,
            new RepresentativeDetails("Nguyễn Thị Mẹ", RepresentativeRelationship.Guardian, null, null, null, "0900000009", null), Now);

        Assert.Equal(RepresentativeRelationship.Guardian, Assert.Single(patient.Representatives).Relationship);
        patient.RemoveRepresentative(representative.Id, Now);
        Assert.Empty(patient.Representatives);
    }

    [Fact]
    public void Representative_WithInconsistentCitizenId_Throws()
    {
        var patient = Patient.Create(Details(), Now);

        Assert.Throws<DomainException>(() => patient.AddRepresentative(
            new RepresentativeDetails("Người giả lập", RepresentativeRelationship.Father, "001085000001", new DateOnly(1960, 1, 1), Gender.Male, null, null),
            Now));
    }
}
