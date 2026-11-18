using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Departments;
using Consent.Application.Tests.Fakes;

namespace Consent.Application.Tests;

/// <summary>Danh mục khoa/phòng lấy từ HIS qua API thông tin người bệnh (quyết định 42). Dữ liệu giả lập.</summary>
public sealed class DepartmentHisSyncTests
{
    [Fact]
    public async Task Sync_AddsNewHisDepartments_AndRenamesThoseWithTheSameCode()
    {
        var ctx = new AppTestContext();
        var kkb = ctx.AddDepartment("K01", "Khoa Khám bệnh cũ");
        ctx.DepartmentSource.Returns(("K01", "Khoa Khám bệnh"), ("K02", "Khoa Nội tổng hợp"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var summary = result.Value!;
        Assert.Equal((2, 1, 1, 0), (summary.Total, summary.Added, summary.Updated, summary.Unchanged));
        Assert.Empty(summary.Deactivated);
        Assert.Empty(summary.Skipped);
        Assert.Equal("Khoa Khám bệnh", kkb.Name);
        Assert.Equal(2, ctx.Store.Departments.Count);
        var added = ctx.Store.Departments.Single(d => d.Code == "K02");
        Assert.Equal("Khoa Nội tổng hợp", added.Name);
        Assert.True(added.IsActive);
    }

    [Fact]
    public async Task Sync_ManualDepartmentWithTheSameName_TakesTheHisCode_AndKeepsItsUsers()
    {
        var ctx = new AppTestContext();
        // Khoa nhập tay trước đây: mã riêng, tên gõ khác hoa thường, thừa khoảng trắng, dạng Unicode tổ hợp.
        var noi = ctx.AddDepartment("NOI", "  khoa   NỘI tổng hợp ".Normalize(NormalizationForm.FormD));
        var doctor = ctx.AddUser("bacsi", departmentId: noi.Id);
        ctx.DepartmentSource.Returns(("K02", "Khoa Nội tổng hợp"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal((0, 1), (result.Value!.Added, result.Value.Updated));
        Assert.Empty(result.Value.Deactivated);
        var department = Assert.Single(ctx.Store.Departments);
        Assert.Equal(noi.Id, department.Id);
        Assert.Equal(("K02", "Khoa Nội tổng hợp", true), (department.Code, department.Name, department.IsActive));
        Assert.Equal(noi.Id, doctor.DepartmentId);
    }

    [Fact]
    public async Task Sync_DoesNotMatchByName_ADepartmentWhoseCodeIsOnHis()
    {
        var ctx = new AppTestContext();
        var k01 = ctx.AddDepartment("K01", "Khoa Nội");
        // K02 đứng trước: khoa K01 có tên trùng nhưng mã có trên HIS, không được nhận mã K02.
        ctx.DepartmentSource.Returns(("K02", "Khoa Nội"), ("K01", "Khoa Khám bệnh"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal((1, 1), (result.Value!.Added, result.Value.Updated));
        Assert.Equal(("K01", "Khoa Khám bệnh"), (k01.Code, k01.Name));
        Assert.Equal("Khoa Nội", ctx.Store.Departments.Single(d => d.Code == "K02").Name);
    }

    [Fact]
    public async Task Sync_DeactivatesDepartmentsNoLongerOnHis_AndReactivatesReturningOnes()
    {
        var ctx = new AppTestContext();
        var kept = ctx.AddDepartment("K01", "Khoa Khám bệnh");
        var gone = ctx.AddDepartment("K09", "Khoa đã giải thể");
        var returning = ctx.AddDepartment("K05", "Khoa Y học cổ truyền", active: false);
        var stillInactive = ctx.AddDepartment("K07", "Khoa cũ đã ngừng", active: false);
        ctx.AddUser("dieuduong", departmentId: gone.Id);
        ctx.DepartmentSource.Returns(("K01", "Khoa Khám bệnh"), ("K05", "Khoa Y học cổ truyền"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal((0, 1, 1), (result.Value!.Added, result.Value.Updated, result.Value.Unchanged));
        Assert.Equal(["Khoa đã giải thể"], result.Value.Deactivated);
        Assert.True(kept.IsActive);
        Assert.True(returning.IsActive);
        Assert.False(gone.IsActive);
        Assert.False(stillInactive.IsActive);
        Assert.Equal(4, ctx.Store.Departments.Count);
    }

    [Fact]
    public async Task Sync_TheSameListTwice_ChangesNothingTheSecondTime()
    {
        var ctx = new AppTestContext();
        ctx.AddDepartment("NOI", "Khoa Nội");
        ctx.DepartmentSource.Returns(("K01", "Khoa Khám bệnh"), ("K02", "Khoa Nội"));
        var service = ctx.CreateDepartmentService();
        await service.SyncFromHisAsync(CancellationToken.None);

        var again = await service.SyncFromHisAsync(CancellationToken.None);

        Assert.True(again.IsSuccess, again.Error?.Message);
        var summary = again.Value!;
        Assert.Equal((2, 0, 0, 2), (summary.Total, summary.Added, summary.Updated, summary.Unchanged));
        Assert.Empty(summary.Deactivated);
        Assert.Equal(2, ctx.Store.Departments.Count);
    }

    [Fact]
    public async Task Sync_SkipsUnusableCodes_IgnoresRepeatedCodes_AndFixesNames()
    {
        var ctx = new AppTestContext();
        var longName = new string('x', 250);
        ctx.DepartmentSource.Returns(
            (" k03 ", "  "),
            ("K04", longName),
            ("MA-KHOA-DAI-HON-HAI-MUOI", "Khoa mã dài (giả lập)"),
            ("K03", "Trùng mã"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value!.Total);
        Assert.Equal(["MA-KHOA-DAI-HON-HAI-MUOI"], result.Value.Skipped);
        Assert.Equal("K03", ctx.Store.Departments.Single(d => d.Code == "K03").Name);
        Assert.Equal(new string('x', 200), ctx.Store.Departments.Single(d => d.Code == "K04").Name);
    }

    [Fact]
    public async Task Sync_WhenHisReturnsNothingUsable_KeepsTheCatalog()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        ctx.DepartmentSource.Returns(("MA-KHOA-DAI-HON-HAI-MUOI", "Khoa mã dài (giả lập)"));

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.Equal("departments.his_empty", result.Error!.Code);
        Assert.True(noi.IsActive);
        Assert.Single(ctx.Store.Departments);
    }

    [Theory]
    [InlineData(DepartmentSourceStatus.NotConfigured, "departments.his_not_configured")]
    [InlineData(DepartmentSourceStatus.Unavailable, "departments.his_unavailable")]
    [InlineData(DepartmentSourceStatus.Disabled, "departments.his_disabled")]
    public async Task Sync_WhenHisCannotBeRead_KeepsTheCatalog(DepartmentSourceStatus status, string error)
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        ctx.DepartmentSource.IsEnabled = true;
        ctx.DepartmentSource.Result = new DepartmentSourceResult(status, [new ExternalDepartment("K01", "Khoa Khám bệnh")]);

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.Equal(error, result.Error!.Code);
        Assert.True(noi.IsActive);
        Assert.Single(ctx.Store.Departments);
    }

    [Fact]
    public async Task Sync_WithoutThePatientInfoApi_IsNotAvailable()
    {
        var ctx = new AppTestContext();

        var result = await ctx.CreateDepartmentService().SyncFromHisAsync(CancellationToken.None);

        Assert.Equal("departments.his_disabled", result.Error!.Code);
        Assert.Equal(0, ctx.DepartmentSource.Calls);
    }

    [Fact]
    public async Task CreateUpdateDelete_WhenDepartmentsComeFromHis_AreRejected()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        ctx.DepartmentSource.IsEnabled = true;
        var service = ctx.CreateDepartmentService();

        var created = await service.CreateAsync(new DepartmentRequest("NGOAI", "Khoa Ngoại"), CancellationToken.None);
        var updated = await service.UpdateAsync(noi.Id, new DepartmentRequest("NOI", "Khoa Nội đổi tên", IsActive: false), CancellationToken.None);
        var deleted = await service.DeleteAsync(noi.Id, CancellationToken.None);

        Assert.Equal("departments.managed_by_his", created.Error!.Code);
        Assert.Equal("departments.managed_by_his", updated.Error!.Code);
        Assert.Equal("departments.managed_by_his", deleted.Error!.Code);
        var department = Assert.Single(ctx.Store.Departments);
        Assert.Equal(("Khoa Nội", true), (department.Name, department.IsActive));
        Assert.True(service.IsManagedByHis);
    }
}
