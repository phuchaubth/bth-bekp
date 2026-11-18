namespace PatientInfo.Api.Tests;

/// <summary>Đọc dòng kết quả của câu truy vấn số liệu trong ngày và kiểm tra cấu hình câu truy vấn (quyết định 31).</summary>
public sealed class DailyStatisticsMappingTests
{
    private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Map_ReadsTheAliasedColumns_AndTrimsText()
    {
        Assert.Equal(
            new DailyStatisticDto("waitingAdmission", "NOIA", 2),
            DailyStatisticsRepository.Map(Row(("indicator", " waitingAdmission "), ("DEPARTMENTCODE", " NOIA "), ("Value", 2))));
        Assert.Equal(
            new DailyStatisticDto("outpatientVisits", null, 686),
            DailyStatisticsRepository.Map(Row(("Indicator", "outpatientVisits"), ("DepartmentCode", "  "), ("Value", 686L))));
    }

    [Theory]
    [InlineData((short)5, 5)]
    [InlineData((byte)5, 5)]
    [InlineData("12", 12)]
    public void Value_AcceptsCommonNumericTypes(object raw, int expected)
    {
        Assert.Equal(expected, DailyStatisticsRepository.Map(Row(("Indicator", "xray"), ("Value", raw)))!.Value);
    }

    [Fact]
    public void Value_AsAWholeDecimal_IsAccepted()
    {
        Assert.Equal(7, DailyStatisticsRepository.Map(Row(("Indicator", "ct"), ("Value", 7m)))!.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void RowsWithoutAnIndicatorOrAWholeNonNegativeValue_AreDropped(Dictionary<string, object?> row)
    {
        Assert.Null(DailyStatisticsRepository.Map(row));
    }

    public static TheoryData<Dictionary<string, object?>> InvalidRows() =>
    [
        Row(("Indicator", null), ("Value", 1)),
        Row(("Indicator", " "), ("Value", 1)),
        Row(("Indicator", "mri")),
        Row(("Indicator", "mri"), ("Value", null)),
        Row(("Indicator", "mri"), ("Value", -1)),
        Row(("Indicator", "mri"), ("Value", 2.5m)),
        Row(("Indicator", "mri"), ("Value", "nhiều")),
        Row(("Indicator", "mri"), ("Value", 5_000_000_000L)),
    ];

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("SELECT 'xray' AS Indicator, NULL AS DepartmentCode, COUNT(*) AS Value FROM dbo.X WHERE T >= @From AND T < @To", true)]
    [InlineData("WITH a AS (SELECT 1 AS n) SELECT 'ct' AS Indicator, n AS Value FROM a WHERE @From < @To", true)]
    [InlineData("SELECT 'xray' AS Indicator, 1 AS Value FROM dbo.X WHERE T >= @From", false)]
    [InlineData("SELECT 'xray' AS Indicator, 1 AS Value; DELETE dbo.X WHERE T >= @From AND T < @To", false)]
    [InlineData("DELETE dbo.X WHERE T >= @From AND T < @To", false)]
    public void Query_MustBeASingleSelectUsingTheDayParameters(string? query, bool valid)
    {
        Assert.Equal(valid, new PatientInfoOptions { DailyStatisticsQuery = query }.HasValidDailyStatisticsQuery);
    }
}
