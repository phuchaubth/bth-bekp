namespace PatientInfo.Api.Tests;

/// <summary>Đọc dòng kết quả của câu truy vấn danh mục khoa/phòng và kiểm tra cấu hình câu truy vấn (quyết định 42).</summary>
public sealed class DepartmentMappingTests
{
    private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Map_ReadsTheAliasedColumns_AndTrimsText()
    {
        Assert.Equal(
            new DepartmentDto("K01", "Khoa Khám bệnh (giả lập)"),
            DepartmentRepository.Map(Row(("code", " K01 "), ("NAME", " Khoa Khám bệnh (giả lập) "))));
        Assert.Equal(new DepartmentDto("12", "Khoa số (giả lập)"), DepartmentRepository.Map(Row(("Code", 12), ("Name", "Khoa số (giả lập)"))));
    }

    [Fact]
    public void Map_UsesTheCode_WhenTheNameIsMissing()
    {
        Assert.Equal(new DepartmentDto("K03", "K03"), DepartmentRepository.Map(Row(("Code", "K03"), ("Name", null))));
        Assert.Equal(new DepartmentDto("K04", "K04"), DepartmentRepository.Map(Row(("Code", "K04"), ("Name", "  "))));
        Assert.Equal(new DepartmentDto("K05", "K05"), DepartmentRepository.Map(Row(("Code", "K05"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RowsWithoutACode_AreDropped(string? code)
    {
        Assert.Null(DepartmentRepository.Map(Row(("Code", code), ("Name", "Khoa không mã (giả lập)"))));
        Assert.Null(DepartmentRepository.Map(Row(("Name", "Khoa không mã (giả lập)"))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("SELECT MaKhoa AS Code, TenKhoa AS Name FROM dbo.X WHERE HoatDong = 1", true)]
    [InlineData("WITH k AS (SELECT MaKhoa, TenKhoa FROM dbo.X) SELECT MaKhoa AS Code, TenKhoa AS Name FROM k;", true)]
    [InlineData("SELECT MaKhoa AS Code FROM dbo.X; DELETE dbo.X", false)]
    [InlineData("DELETE dbo.X", false)]
    [InlineData("EXEC dbo.LayKhoa", false)]
    public void Query_MustBeASingleSelect(string? query, bool valid)
    {
        Assert.Equal(valid, new PatientInfoOptions { DepartmentQuery = query }.HasValidDepartmentQuery);
    }
}
