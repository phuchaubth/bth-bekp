namespace PatientInfo.Api.Tests;

/// <summary>Đọc dòng kết quả của câu truy vấn lượt tiếp nhận và kiểm tra cấu hình câu truy vấn (quyết định 25).</summary>
public sealed class EncounterRepositoryMappingTests
{
    private static readonly DateTime Morning = new(2026, 11, 11, 7, 30, 0);

    private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Map_ReadsTheAliasedColumns_TrimmingTextAndDroppingBlanks()
    {
        var encounter = EncounterRepository.Map(Row(
            ("encounterno", " TN-0001 "), ("ReceivedAt", Morning), ("Place", "Phòng khám 1"), ("ReceivedBy", "  ")));

        Assert.Equal(new EncounterDto("TN-0001", Morning, "Phòng khám 1", null), encounter);
        Assert.Equal(DateTimeKind.Unspecified, encounter!.ReceivedAt.Kind);
    }

    [Theory]
    [InlineData("11/11/2026 07:30:00")]
    [InlineData("11/11/2026 07:30")]
    [InlineData("2026-11-11 07:30:00")]
    [InlineData("2026-11-11T07:30:00")]
    [InlineData("20261111073000")]
    public void ReceivedAt_StoredAsText_AcceptsCommonFormats(string raw)
    {
        Assert.Equal(Morning, EncounterRepository.Map(Row(("EncounterNo", "TN-0001"), ("ReceivedAt", raw)))!.ReceivedAt);
    }

    [Fact]
    public void ReceivedAt_WithAnOffset_KeepsTheHospitalClockTime_AndNumericNumbersBecomeText()
    {
        var encounter = EncounterRepository.Map(Row(
            ("EncounterNo", 260001), ("ReceivedAt", new DateTimeOffset(Morning, TimeSpan.FromHours(7)))));

        Assert.Equal(("260001", Morning), (encounter!.EncounterNo, encounter.ReceivedAt));
    }

    [Fact]
    public void RowsWithoutANumberOrAReadableTime_AreSkipped()
    {
        Assert.Null(EncounterRepository.Map(Row(("ReceivedAt", Morning))));
        Assert.Null(EncounterRepository.Map(Row(("EncounterNo", " "), ("ReceivedAt", Morning))));
        Assert.Null(EncounterRepository.Map(Row(("EncounterNo", "TN-0001"))));
        Assert.Null(EncounterRepository.Map(Row(("EncounterNo", "TN-0001"), ("ReceivedAt", "sáng nay"))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("SELECT SoTiepNhan AS EncounterNo FROM dbo.TiepNhan WHERE MaBN = @PatientCode AND ThoiGian >= @From AND ThoiGian < @To", true)]
    [InlineData("WITH t AS (SELECT * FROM dbo.TiepNhan) SELECT * FROM t WHERE SoCCCD = @CitizenId AND ThoiGian >= @From AND ThoiGian < @To;", true)]
    [InlineData("SELECT * FROM dbo.TiepNhan WHERE MaBN = @PatientCode", false)]
    [InlineData("SELECT * FROM dbo.TiepNhan WHERE ThoiGian >= @From AND ThoiGian < @To", false)]
    [InlineData("UPDATE dbo.TiepNhan SET MaBN = @PatientCode WHERE ThoiGian >= @From AND ThoiGian < @To", false)]
    [InlineData("SELECT * FROM dbo.TiepNhan WHERE MaBN = @PatientCode AND ThoiGian >= @From AND ThoiGian < @To; DROP TABLE dbo.TiepNhan", false)]
    public void EncounterQuery_IsOptional_ButMustBeASingleSelectUsingTheDatesAndAPatientParameter(string? query, bool valid)
    {
        var options = new PatientInfoOptions { EncounterQuery = query };

        Assert.Equal(valid, options.HasValidEncounterQuery);
        Assert.Equal(query is not null, options.HasEncounterQuery);
    }
}
