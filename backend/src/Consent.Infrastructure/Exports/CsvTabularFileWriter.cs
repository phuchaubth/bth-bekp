using System.Globalization;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Domain.Exports;

namespace Consent.Infrastructure.Exports;

/// <summary>
/// Ghi CSV theo RFC 4180: UTF-8 có BOM (để Excel nhận đúng tiếng Việt), phân cách bằng dấu phẩy, xuống dòng CRLF.
/// Chống chèn công thức (CSV injection): ô văn bản bắt đầu bằng <c>= + - @</c>, tab hoặc CR được thêm dấu nháy đơn
/// phía trước, để Excel/LibreOffice hiểu là chữ chứ không chạy như công thức. Ngày: dd/MM/yyyy; ngày giờ: dd/MM/yyyy HH:mm.
/// </summary>
internal sealed class CsvTabularFileWriter : ITabularFileWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public ExportFormat Format => ExportFormat.Csv;

    public string ContentType => "text/csv; charset=utf-8";

    public string FileExtension => "csv";

    public async Task<int> WriteAsync(TabularData data, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);
        await using var writer = new StreamWriter(output, Utf8WithBom, bufferSize: 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };

        // Tiêu đề cột nội dung lấy từ nhãn của mẫu phiếu nên cũng được chống chèn công thức.
        await writer.WriteLineAsync(string.Join(',', data.Columns.Select(c => Escape(NeutralizeFormula(c.Header))))).ConfigureAwait(false);
        var count = 0;
        var line = new StringBuilder();
        await foreach (var row in data.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            line.Clear();
            for (var c = 0; c < data.Columns.Count; c++)
            {
                if (c > 0)
                {
                    line.Append(',');
                }

                line.Append(Escape(c < row.Count ? FormatValue(row[c]) : null));
            }

            await writer.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
            count++;
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => NeutralizeFormula(text),
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "Có" : "Không",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => NeutralizeFormula(value.ToString() ?? string.Empty),
    };

    /// <summary>Ô văn bản có thể bị chương trình bảng tính hiểu là công thức thì thêm dấu nháy đơn phía trước.</summary>
    internal static string NeutralizeFormula(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    /// <summary>Đặt trong ngoặc kép khi có dấu phẩy, ngoặc kép, xuống dòng hoặc khoảng trắng đầu/cuối.</summary>
    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || value[0] == ' ' || value[^1] == ' ';
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
