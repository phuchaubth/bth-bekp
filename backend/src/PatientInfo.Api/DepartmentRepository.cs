using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Một khoa/phòng trong danh mục của HIS: mã và tên.</summary>
public sealed record DepartmentDto(string Code, string Name);

/// <summary>
/// Đọc danh mục khoa/phòng của HIS bằng câu truy vấn cấu hình được (quyết định 42). Kết quả không có thông tin người bệnh.
/// </summary>
public sealed class DepartmentRepository(IOptions<PatientInfoOptions> options)
{
    /// <summary>Đọc tối đa <paramref name="limit"/> khoa/phòng (dòng không có mã thì bỏ).</summary>
    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = settings.DepartmentQuery!.Trim().TrimEnd(';');
        command.CommandTimeout = Math.Max(1, settings.QueryTimeoutSeconds);

        var result = new List<DepartmentDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (result.Count < limit && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i), StringComparer.OrdinalIgnoreCase);
            if (Map(row) is { } department)
            {
                result.Add(department);
            }
        }

        return result;
    }

    /// <summary>Đổi một dòng kết quả thành khoa/phòng: thiếu mã thì bỏ dòng; thiếu tên thì lấy mã làm tên.</summary>
    public static DepartmentDto? Map(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Text(row, "Code") is { } code ? new DepartmentDto(code, Text(row, "Name") ?? code) : null;
    }

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text
            ? text
            : null;
}
