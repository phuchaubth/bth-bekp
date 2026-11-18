using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Một số liệu trong ngày: mã chỉ số, mã khoa (chỉ số theo khoa), giá trị.</summary>
public sealed record DailyStatisticDto(string Indicator, string? DepartmentCode, int Value);

/// <summary>
/// Đọc số liệu trong ngày cho bảng tổng hợp điều dưỡng bằng câu truy vấn cấu hình được (quyết định 31); ngày luôn truyền qua
/// tham số. Kết quả chỉ là số đếm, không có thông tin người bệnh.
/// </summary>
public sealed class DailyStatisticsRepository(IOptions<PatientInfoOptions> options)
{
    public async Task<IReadOnlyList<DailyStatisticDto>> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = settings.DailyStatisticsQuery!.Trim().TrimEnd(';');
        command.CommandTimeout = Math.Max(1, settings.QueryTimeoutSeconds);
        command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime2) { Value = date.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime2) { Value = date.AddDays(1).ToDateTime(TimeOnly.MinValue) });

        var result = new List<DailyStatisticDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && result.Count < PatientInfoProgram.MaxDailyStatistics)
        {
            var row = Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i), StringComparer.OrdinalIgnoreCase);
            if (Map(row) is { } statistic)
            {
                result.Add(statistic);
            }
        }

        return result;
    }

    /// <summary>Đổi một dòng kết quả thành số liệu; thiếu mã chỉ số hoặc giá trị không phải số nguyên ≥ 0 thì bỏ dòng.</summary>
    public static DailyStatisticDto? Map(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var indicator = Text(row, "Indicator");
        var value = row.TryGetValue("Value", out var raw)
            ? raw switch
            {
                int number => number,
                long number when number is >= 0 and <= int.MaxValue => (int)number,
                short number => number,
                byte number => number,
                decimal number when number >= 0 && number <= int.MaxValue && decimal.Truncate(number) == number => (int)number,
                string text when int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => (int?)null,
            }
            : null;
        return indicator is null || value is not >= 0 ? null : new DailyStatisticDto(indicator, Text(row, "DepartmentCode"), value.Value);
    }

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text
            ? text
            : null;
}
