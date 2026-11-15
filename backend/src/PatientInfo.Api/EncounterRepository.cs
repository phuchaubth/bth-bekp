using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Một lượt tiếp nhận người bệnh trên HIS (thời gian là giờ của bệnh viện).</summary>
public sealed record EncounterDto(string EncounterNo, DateTime ReceivedAt, string? Place, string? ReceivedBy);

/// <summary>Đọc lượt tiếp nhận bằng câu truy vấn cấu hình được; giá trị luôn truyền qua tham số (không ghép chuỗi).</summary>
public sealed class EncounterRepository(IOptions<PatientInfoOptions> options)
{
    private static readonly string[] DateTimeFormats =
        ["dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyyMMddHHmmss"];

    /// <summary>Các lượt tiếp nhận từ <paramref name="from"/> đến hết ngày <paramref name="to"/>, mới nhất trước, tối đa 200 lượt.</summary>
    public async Task<IReadOnlyList<EncounterDto>> SearchAsync(
        string? patientCode, string? citizenId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = settings.EncounterQuery!.Trim().TrimEnd(';');
        command.CommandTimeout = Math.Max(1, settings.QueryTimeoutSeconds);
        command.Parameters.Add(new SqlParameter("@PatientCode", SqlDbType.NVarChar, 50) { Value = (object?)patientCode ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@CitizenId", SqlDbType.NVarChar, 20) { Value = (object?)citizenId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime2) { Value = from.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime2) { Value = to.AddDays(1).ToDateTime(TimeOnly.MinValue) });

        var result = new List<EncounterDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i), StringComparer.OrdinalIgnoreCase);
            if (Map(row) is { } encounter)
            {
                result.Add(encounter);
            }
        }

        return [.. result.OrderByDescending(e => e.ReceivedAt).Take(PatientInfoProgram.MaxEncounters)];
    }

    /// <summary>Đổi một dòng kết quả thành lượt tiếp nhận; thiếu số tiếp nhận hoặc thời gian thì bỏ dòng.</summary>
    public static EncounterDto? Map(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var number = Text(row, "EncounterNo");
        var receivedAt = row.TryGetValue("ReceivedAt", out var value)
            ? value switch
            {
                DateTime dateTime => dateTime,
                DateTimeOffset offset => offset.DateTime,
                string text when DateTime.TryParseExact(text.Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
                _ => (DateTime?)null,
            }
            : null;
        return number is null || receivedAt is null
            ? null
            : new EncounterDto(number, DateTime.SpecifyKind(receivedAt.Value, DateTimeKind.Unspecified), Text(row, "Place"), Text(row, "ReceivedBy"));
    }

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text
            ? text
            : null;
}
