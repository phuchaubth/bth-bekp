using Consent.Domain.Common;
using Consent.Domain.Departments;

namespace Consent.Domain.Tests;

public sealed class DepartmentTests
{
    [Fact]
    public void Create_NormalizesCodeToUpperCase_AndIsActive()
    {
        var department = Department.Create(" kkb ", " Khoa Khám bệnh ");

        Assert.Equal("KKB", department.Code);
        Assert.Equal("Khoa Khám bệnh", department.Name);
        Assert.True(department.IsActive);
    }

    [Fact]
    public void Update_ChangesValues_IncludingActiveFlag()
    {
        var department = Department.Create("KKB", "Khoa Khám bệnh");

        department.Update("noi", "Khoa Nội", isActive: false);

        Assert.Equal("NOI", department.Code);
        Assert.Equal("Khoa Nội", department.Name);
        Assert.False(department.IsActive);
    }

    [Fact]
    public void SyncFromHis_TakesTheHisCodeAndName_ReactivatesIt_AndReportsWhetherAnythingChanged()
    {
        var department = Department.Create("NOI", "Khoa Nội");
        department.Deactivate();
        Assert.False(department.IsActive);

        Assert.True(department.SyncFromHis(" k02 ", " Khoa Nội tổng hợp "));
        Assert.Equal(("K02", "Khoa Nội tổng hợp", true), (department.Code, department.Name, department.IsActive));

        Assert.False(department.SyncFromHis("K02", "Khoa Nội tổng hợp"));
        Assert.True(department.SyncFromHis("K02", "Khoa Nội"));
        department.Deactivate();
        Assert.True(department.SyncFromHis("K02", "Khoa Nội"));
    }

    [Theory]
    [InlineData("", "Tên")]
    [InlineData("KKB", " ")]
    [InlineData("MA_KHOA_QUA_DAI_HON_20_KY_TU", "Tên")]
    public void Create_WithInvalidValues_Throws(string code, string name)
    {
        Assert.Throws<DomainException>(() => Department.Create(code, name));
    }

    [Fact]
    public void ShortName_IsTrimmed_KeepsItsCase_AndBlankClearsIt()
    {
        var department = Department.Create("NOIA", "Khoa Nội A (giả lập)");
        Assert.Null(department.ShortName);

        department.SetShortName("  Nội A  ");
        Assert.Equal("Nội A", department.ShortName);

        department.SetShortName(new string('x', Department.ShortNameMaxLength));
        Assert.Equal(Department.ShortNameMaxLength, department.ShortName!.Length);

        // Gõ kiểu tổ hợp (một số bộ gõ tiếng Việt) thì lưu dạng dựng sẵn, như tên khoa lấy từ HIS.
        department.SetShortName("No\u0302\u0323i A");
        Assert.Equal("N\u1ED9i A", department.ShortName);

        department.SetShortName("   ");
        Assert.Null(department.ShortName);
        department.SetShortName("HSTC");
        department.SetShortName(null);
        Assert.Null(department.ShortName);
    }

    [Fact]
    public void ShortName_IsKept_WhenTheHisListUpdatesTheDepartment()
    {
        var department = Department.Create("NOIA", "Khoa Nội A (giả lập)");
        department.SetShortName("Nội A");

        department.SyncFromHis("NOIA", "Khoa Nội tổng hợp A (giả lập)");
        department.Update("NOIA2", "Khoa Nội A (giả lập)", isActive: true);

        Assert.Equal("Nội A", department.ShortName);
    }

    [Theory]
    [InlineData("Tên viết tắt quá dài hơn 20")]
    [InlineData("Nội\nA")]
    [InlineData("Nội\tA")]
    public void ShortName_TooLongOrWithControlCharacters_IsRejected_AndTheOldOneKept(string shortName)
    {
        var department = Department.Create("NOIA", "Khoa Nội A (giả lập)");
        department.SetShortName("Nội A");

        var error = Assert.Throws<DomainException>(() => department.SetShortName(shortName));

        Assert.Contains($"tối đa {Department.ShortNameMaxLength} ký tự", error.Message, StringComparison.Ordinal);
        Assert.Equal("Nội A", department.ShortName);
    }
}
