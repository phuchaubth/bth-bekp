using System.Globalization;
using System.Text.Json.Nodes;
using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Common;
using Consent.Domain.ConsentTemplates;

namespace Consent.Application.Exports;

/// <summary>
/// Cột nội dung phiếu theo mẫu (mỗi trường của mẫu một cột; trường điểm có thêm cột "mức"). Gộp các trường của mọi phiên
/// bản mẫu, theo thứ tự của phiên bản mới nhất. Mỗi phiếu được đọc theo đúng phiên bản của nó và chỉ lấy trường đang
/// hiển thị (giống nội dung in trên PDF).
/// Dữ liệu nhạy cảm bị che, trừ khi xuất đầy đủ: trường tự điền số thẻ BHYT/số điện thoại bị che một phần; bảng nhiều dòng
/// (ví dụ người được nhận thông tin, có họ tên và số điện thoại) chỉ ghi số dòng.
/// </summary>
internal sealed class TemplateContentColumns
{
    private const string ScoreLevelSuffix = " – Mức";

    private readonly Dictionary<Guid, VersionDefinition> _versions;
    private readonly List<ContentColumn> _columns;

    private TemplateContentColumns(Dictionary<Guid, VersionDefinition> versions, List<ContentColumn> columns)
    {
        _versions = versions;
        _columns = columns;
    }

    public IReadOnlyList<TabularColumn> Columns => [.. _columns.Select(c => new TabularColumn(c.Header, c.Kind, c.Width))];

    public static TemplateContentColumns Create(ConsentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var versions = new Dictionary<Guid, VersionDefinition>();
        var columns = new List<ContentColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var version in template.Versions.OrderByDescending(v => v.VersionNo))
        {
            var definition = ConsentTemplateService.ReadDefinition(version);
            var blocks = definition.InputBlocks.ToDictionary(b => b.Key!, StringComparer.Ordinal);
            versions[version.Id] = new VersionDefinition(definition, blocks);
            foreach (var block in definition.InputBlocks.Where(b => seen.Add(b.Key!)))
            {
                var header = string.IsNullOrWhiteSpace(block.Label) ? block.Key! : block.Label.Trim();
                if (block.Type == BlockTypes.Score)
                {
                    columns.Add(new ContentColumn(block.Key!, header, TabularCellKind.Number, 12, ContentPart.ScoreTotal));
                    columns.Add(new ContentColumn(block.Key!, header + ScoreLevelSuffix, TabularCellKind.Text, 18, ContentPart.ScoreLevel));
                    continue;
                }

                var (kind, width) = block.Type switch
                {
                    BlockTypes.Date => (TabularCellKind.Date, 11d),
                    BlockTypes.DateTime => (TabularCellKind.DateTime, 17d),
                    BlockTypes.Confirm => (TabularCellKind.Text, 12d),
                    _ => (TabularCellKind.Text, 24d),
                };
                columns.Add(new ContentColumn(block.Key!, header, kind, width, ContentPart.Value));
            }
        }

        return new TemplateContentColumns(versions, columns);
    }

    /// <summary>Giá trị các cột nội dung của một phiếu (phiếu của phiên bản không xác định: để trống).</summary>
    public IEnumerable<object?> Values(Guid templateVersionId, string? dataJson, bool includeSensitiveData)
    {
        if (!_versions.TryGetValue(templateVersionId, out var version))
        {
            return _columns.Select(_ => (object?)null);
        }

        var evaluation = FormDataEvaluator.Evaluate(version.Definition, FormDataEvaluator.ParseData(dataJson));
        return _columns.Select(column =>
        {
            if (!version.Blocks.TryGetValue(column.Key, out var block))
            {
                return null;
            }

            return column.Part switch
            {
                ContentPart.ScoreTotal => evaluation.Scores.TryGetValue(column.Key, out var score) ? score.Total : null,
                ContentPart.ScoreLevel => evaluation.Scores.TryGetValue(column.Key, out var score) && score.Total is not null
                    ? score.LevelLabel
                    : null,
                _ => Format(block, evaluation.Data[column.Key], includeSensitiveData),
            };
        });
    }

    private static object? Format(TemplateBlock block, JsonNode? value, bool includeSensitiveData)
    {
        if (value is null)
        {
            return null;
        }

        switch (block.Type)
        {
            case BlockTypes.Choice:
                return value is JsonValue choice && choice.TryGetValue<string>(out var selected) ? OptionLabel(block, selected) : null;
            case BlockTypes.MultiChoice:
                return value is JsonArray array
                    ? string.Join("; ", array.Select(v => v is JsonValue item && item.TryGetValue<string>(out var text) ? OptionLabel(block, text) : null)
                        .Where(text => !string.IsNullOrEmpty(text)))
                    : null;
            case BlockTypes.Confirm:
                return value is JsonValue confirm && confirm.TryGetValue<bool>(out var flag) && flag ? "Đã xác nhận" : null;
            case BlockTypes.Date:
                return value is JsonValue date && date.TryGetValue<string>(out var dateText)
                    && DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                        ? parsedDate
                        : null;
            case BlockTypes.DateTime:
                return value is JsonValue time && time.TryGetValue<string>(out var timeText)
                    && DateTime.TryParseExact(timeText, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
                        ? parsedTime
                        : null;
            case BlockTypes.Repeater:
                return value is JsonArray rows ? FormatRows(block, rows, includeSensitiveData) : null;
            default:
                var text = value is JsonValue textValue && textValue.TryGetValue<string>(out var raw) ? raw : value.ToJsonString();
                return includeSensitiveData ? text : MaskPrefilled(block, text);
        }
    }

    private static string OptionLabel(TemplateBlock block, string value) =>
        block.Options?.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal))?.Label ?? value;

    /// <summary>Bảng nhiều dòng: đầy đủ "Cột: giá trị, …" từng dòng (nối bằng " | "), hoặc chỉ số dòng khi che dữ liệu.</summary>
    private static string FormatRows(TemplateBlock block, JsonArray rows, bool includeSensitiveData)
    {
        if (!includeSensitiveData)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{rows.Count} dòng");
        }

        var columns = block.Columns ?? [];
        return string.Join(" | ", rows.OfType<JsonObject>().Select(row => string.Join(", ", columns
            .Select(c => (c.Label, Value: row[c.Key] is JsonValue cell && cell.TryGetValue<string>(out var text) ? text : null))
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .Select(c => $"{c.Label}: {c.Value}"))));
    }

    private static string? MaskPrefilled(TemplateBlock block, string text) => block.Prefill switch
    {
        PrefillSources.HealthInsuranceNumber => SensitiveMask.HealthInsuranceNumber(text),
        PrefillSources.PhoneNumber => SensitiveMask.PhoneNumber(text),
        _ => text,
    };

    private enum ContentPart
    {
        Value,
        ScoreTotal,
        ScoreLevel,
    }

    private sealed record ContentColumn(string Key, string Header, TabularCellKind Kind, double Width, ContentPart Part);

    private sealed record VersionDefinition(TemplateDefinition Definition, Dictionary<string, TemplateBlock> Blocks);
}
