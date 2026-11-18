using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Thông tin hành chính người bệnh trả về cho phần mềm gọi API (trường nào thiếu thì null).</summary>
public sealed record PatientInfoDto(
    string? PatientCode,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? CitizenId,
    string? PhoneNumber,
    string? PermanentAddress,
    string? CurrentAddress,
    string? Ethnicity,
    string? Occupation,
    string? Nationality,
    string? HealthInsuranceNumber,
    DateOnly? CitizenIdIssueDate,
    string? OldIdNumber);

/// <summary>Đọc người bệnh bằng câu truy vấn cấu hình được; giá trị luôn truyền qua tham số (không ghép chuỗi).</summary>
public sealed class PatientRepository(IOptions<PatientInfoOptions> options)
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyyMMdd", "dd-MM-yyyy"];

    public async Task<PatientInfoDto?> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = settings.Query!.Trim().TrimEnd(';');
        command.CommandTimeout = Math.Max(1, settings.QueryTimeoutSeconds);
        command.Parameters.Add(new SqlParameter("@PatientCode", SqlDbType.NVarChar, 50) { Value = (object?)patientCode ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@CitizenId", SqlDbType.NVarChar, 20) { Value = (object?)citizenId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i), StringComparer.OrdinalIgnoreCase);
        return Map(row, settings);
    }

    /// <summary>Đổi một dòng kết quả (theo tên cột) thành thông tin người bệnh; giới tính trả về "Male"/"Female".</summary>
    public static PatientInfoDto Map(IReadOnlyDictionary<string, object?> row, PatientInfoOptions settings)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(settings);
        return new(
            Text(row, "PatientCode"),
            Text(row, "FullName"),
            Date(row, "DateOfBirth"),
            GenderOf(Text(row, "Gender"), settings),
            Text(row, "CitizenId"),
            Text(row, "PhoneNumber"),
            Text(row, "PermanentAddress"),
            Text(row, "CurrentAddress"),
            Text(row, "Ethnicity"),
            Text(row, "Occupation"),
            Text(row, "Nationality"),
            Text(row, "HealthInsuranceNumber")?.ToUpperInvariant(),
            Date(row, "CitizenIdIssueDate"),
            Text(row, "OldIdNumber"));
    }

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text
            ? text
            : null;

    private static DateOnly? Date(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value)
            ? value switch
            {
                DateTime dateTime => DateOnly.FromDateTime(dateTime),
                DateTimeOffset offset => DateOnly.FromDateTime(offset.Date),
                DateOnly date => date,
                string text when DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
                _ => null,
            }
            : null;

    private static string? GenderOf(string? value, PatientInfoOptions settings)
    {
        if (value is null)
        {
            return null;
        }

        if (settings.EffectiveMaleValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)))
        {
            return "Male";
        }

        return settings.EffectiveFemaleValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ? "Female" : null;
    }
}
