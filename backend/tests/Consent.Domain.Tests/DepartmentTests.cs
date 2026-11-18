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
}
