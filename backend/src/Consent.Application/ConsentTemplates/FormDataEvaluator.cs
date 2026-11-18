using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consent.Application.ConsentTemplates;

/// <summary>Tổng điểm và mức của một trường tự tính (ví dụ thang điểm Morse). Chưa trả lời đủ câu hỏi thì <c>Total</c> rỗng.</summary>
public sealed record ScoreResult(int? Total, string? LevelCode, string? LevelLabel);

/// <summary>Kết quả đánh giá dữ liệu phiếu theo định nghĩa mẫu.</summary>
public sealed record FormEvaluation(
    JsonObject Data,
    IReadOnlyDictionary<string, string[]> Errors,
    IReadOnlyDictionary<string, ScoreResult> Scores)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Chuẩn hóa và kiểm tra dữ liệu phiếu theo định nghĩa mẫu (máy chủ là nơi kiểm tra cuối cùng):
/// bỏ trường lạ và trường đang ẩn, cắt khoảng trắng, kiểm tra bắt buộc/định dạng/lựa chọn, tính tổng điểm và mức.
/// Các khối được xử lý theo thứ tự, nên điều kiện hiển thị và tổng điểm dùng giá trị đã chuẩn hóa của trường đứng trước.
/// </summary>
public static class FormDataEvaluator
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <param name="keepHidden">
    /// Lưu nháp: giữ cả giá trị của trường đang ẩn (ví dụ số thẻ BHYT điền sẵn khi chưa chọn hình thức khám) nhưng không
    /// kiểm tra chúng. Trình ký: bỏ trường ẩn để nội dung đem ký chỉ gồm những gì hiển thị trên phiếu.
    /// </param>
    public static FormEvaluation Evaluate(TemplateDefinition definition, JsonObject? input, bool keepHidden = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        input ??= [];
        // Giá trị của trường đang hiển thị: dùng cho điều kiện hiển thị và tổng điểm.
        var visibleValues = new JsonObject();
        var output = new JsonObject();
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var scores = new Dictionary<string, ScoreResult>(StringComparer.Ordinal);
        var blocks = new Dictionary<string, TemplateBlock>(StringComparer.Ordinal);

        foreach (var block in definition.InputBlocks)
        {
            var key = block.Key!;
            blocks[key] = block;
            if (block.Type == BlockTypes.Score)
            {
                scores[key] = ComputeScore(block, visibleValues, blocks);
                continue;
            }

            if (block.Type == BlockTypes.Computed)
            {
                // Luôn tự tính từ các trường đầu vào đang hiển thị; bỏ qua giá trị người dùng gửi lên.
                if (IsVisible(block.VisibleWhen, visibleValues, scores, blocks)
                    && ComputedFormulas.Compute(
                        block.Formula!, [.. block.Inputs!.Select(k => visibleValues[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)])
                        is { } computed)
                {
                    visibleValues[key] = computed;
                    output[key] = computed;
                }

                continue;
            }

            // Câu hỏi tự chọn theo giá trị số của trường đứng trước (ví dụ mức BMI): bỏ qua lựa chọn người dùng gửi lên.
            var raw = block.ValueFrom is not null && DerivedOption(block, visibleValues) is { } derived
                ? JsonValue.Create(derived)
                : input[key];
            var value = Normalize(block, raw, out var typeError);
            if (!IsVisible(block.VisibleWhen, visibleValues, scores, blocks))
            {
                if (keepHidden && value is not null && typeError is null)
                {
                    output[key] = value;
                }

                continue;
            }

            if (typeError is not null)
            {
                Add(errors, key, typeError);
                continue;
            }

            foreach (var message in Check(block, value))
            {
                Add(errors, key, message);
            }

            if (value is not null)
            {
                visibleValues[key] = value.DeepClone();
                output[key] = value;
            }
        }

        return new FormEvaluation(
            output,
            errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal),
            scores);
    }

    /// <summary>Đánh giá lại khi chỉ cần điểm (ví dụ để in PDF), bỏ qua lỗi.</summary>
    public static IReadOnlyDictionary<string, ScoreResult> Scores(TemplateDefinition definition, JsonObject? data) =>
        Evaluate(definition, data).Scores;

    private static JsonNode? Normalize(TemplateBlock block, JsonNode? raw, out string? error)
    {
        error = null;
        if (raw is null)
        {
            return null;
        }

        switch (block.Type)
        {
            case BlockTypes.Confirm:
                if (raw is JsonValue v && v.TryGetValue<bool>(out var flag))
                {
                    return flag ? JsonValue.Create(true) : null;
                }

                break;
            case BlockTypes.MultiChoice:
                if (raw is JsonArray array && array.All(i => i is JsonValue iv && iv.TryGetValue<string>(out _)))
                {
                    var values = array.Select(i => i!.GetValue<string>().Trim()).Where(s => s.Length > 0)
                        .Distinct(StringComparer.Ordinal).ToList();
                    if (values.Count == 0)
                    {
                        return null;
                    }

                    var normalized = new JsonArray();
                    values.ForEach(item => normalized.Add(item));
                    return normalized;
                }

                break;
            case BlockTypes.Repeater:
                return NormalizeRows(block, raw, out error);
            default:
                if (raw is JsonValue sv && sv.TryGetValue<string>(out var text))
                {
                    text = text.Trim();
                    return text.Length == 0 ? null : JsonValue.Create(text);
                }

                break;
        }

        error = "Giá trị không hợp lệ.";
        return null;
    }

    /// <summary>Lựa chọn có khoảng [min, max) chứa giá trị số của trường nguồn; chưa có giá trị hoặc không khớp thì null.</summary>
    internal static string? DerivedOption(TemplateBlock block, JsonObject visibleValues)
    {
        var source = visibleValues[block.ValueFrom!] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
        if (ComputedFormulas.ParseNumber(source) is not { } number)
        {
            return null;
        }

        return block.Options?.FirstOrDefault(o => (o.Min is null || number >= o.Min) && (o.Max is null || number < o.Max))?.Value;
    }

    private static JsonArray? NormalizeRows(TemplateBlock block, JsonNode raw, out string? error)
    {
        error = null;
        if (raw is not JsonArray rows || rows.Any(r => r is not JsonObject))
        {
            error = "Giá trị không hợp lệ.";
            return null;
        }

        var result = new JsonArray();
        foreach (var row in rows.Cast<JsonObject>())
        {
            var normalized = new JsonObject();
            foreach (var column in block.Columns!)
            {
                if (row[column.Key] is JsonValue cell && cell.TryGetValue<string>(out var text) && text.Trim().Length > 0)
                {
                    normalized[column.Key] = text.Trim();
                }
            }

            // Bỏ dòng trống hoàn toàn (người dùng thêm dòng nhưng không nhập gì).
            if (normalized.Count > 0)
            {
                result.Add(normalized);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static IEnumerable<string> Check(TemplateBlock block, JsonNode? value)
    {
        if (value is null)
        {
            if (block.Required)
            {
                yield return block.Type switch
                {
                    BlockTypes.Choice => "Vui lòng chọn một phương án.",
                    BlockTypes.MultiChoice => block.RequireAll ? "Phải đánh dấu đủ tất cả các mục." : "Vui lòng chọn ít nhất một mục.",
                    BlockTypes.Confirm => "Cần xác nhận nội dung này.",
                    BlockTypes.Repeater => $"Nhập ít nhất {Math.Max(1, block.MinItems ?? 1)} dòng.",
                    _ => "Vui lòng nhập thông tin này.",
                };
            }

            yield break;
        }

        switch (block.Type)
        {
            case BlockTypes.Text:
                foreach (var message in CheckText(value.GetValue<string>(), block.MaxLength, block.Pattern, block.PatternMessage))
                {
                    yield return message;
                }

                break;
            case BlockTypes.Date:
                if (!DateOnly.TryParseExact(value.GetValue<string>(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    yield return "Ngày không hợp lệ.";
                }

                break;
            case BlockTypes.DateTime:
                if (!DateTime.TryParseExact(value.GetValue<string>(), DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    yield return "Thời gian không hợp lệ.";
                }

                break;
            case BlockTypes.Choice:
                if (block.Options!.All(o => o.Value != value.GetValue<string>()))
                {
                    yield return "Lựa chọn không hợp lệ.";
                }

                break;
            case BlockTypes.MultiChoice:
                var selected = value.AsArray().Select(v => v!.GetValue<string>()).ToList();
                if (selected.Any(s => block.Options!.All(o => o.Value != s)))
                {
                    yield return "Lựa chọn không hợp lệ.";
                }
                else if (block.RequireAll && selected.Count != block.Options!.Count)
                {
                    yield return "Phải đánh dấu đủ tất cả các mục.";
                }
                else if (block.MinItems is { } min && selected.Count < min)
                {
                    yield return $"Chọn ít nhất {min} mục.";
                }
                else if (block.MaxItems is { } max && selected.Count > max)
                {
                    yield return $"Chọn tối đa {max} mục.";
                }

                break;
            case BlockTypes.Repeater:
                foreach (var message in CheckRows(block, value.AsArray()))
                {
                    yield return message;
                }

                break;
        }
    }

    private static IEnumerable<string> CheckRows(TemplateBlock block, JsonArray rows)
    {
        if (block.MinItems is { } min && rows.Count < min)
        {
            yield return $"Nhập ít nhất {min} dòng.";
        }

        if (block.MaxItems is { } max && rows.Count > max)
        {
            yield return $"Tối đa {max} dòng.";
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]!.AsObject();
            foreach (var column in block.Columns!)
            {
                var cell = row[column.Key]?.GetValue<string>();
                if (cell is null)
                {
                    if (column.Required)
                    {
                        yield return $"Dòng {i + 1}: thiếu {column.Label.ToLowerInvariant()}.";
                    }

                    continue;
                }

                foreach (var message in CheckText(cell, column.MaxLength, column.Pattern, column.PatternMessage))
                {
                    yield return $"Dòng {i + 1} – {column.Label}: {message}";
                }
            }
        }
    }

    private static IEnumerable<string> CheckText(string text, int? maxLength, string? pattern, string? patternMessage)
    {
        if (text.Length > (maxLength ?? TemplateDefinitionValidator.MaxTextLength))
        {
            yield return $"Tối đa {maxLength ?? TemplateDefinitionValidator.MaxTextLength} ký tự.";
        }
        else if (pattern is not null && !Matches(text, pattern))
        {
            yield return patternMessage ?? "Không đúng định dạng.";
        }
    }

    private static bool Matches(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsVisible(
        TemplateCondition? condition,
        JsonObject values,
        Dictionary<string, ScoreResult> scores,
        Dictionary<string, TemplateBlock> blocks)
    {
        if (condition is null)
        {
            return true;
        }

        if (condition.MinLevel is not null)
        {
            if (!scores.TryGetValue(condition.Field, out var score) || score.LevelCode is null
                || !blocks.TryGetValue(condition.Field, out var scoreBlock))
            {
                return false;
            }

            var levels = scoreBlock.Levels!.Select(l => l.Code).ToList();
            return levels.IndexOf(score.LevelCode) >= levels.IndexOf(condition.MinLevel);
        }

        var accepted = condition.In ?? [];
        return values[condition.Field] switch
        {
            JsonArray array => array.Any(v => accepted.Contains(v!.GetValue<string>())),
            JsonValue v when v.TryGetValue<bool>(out var flag) => accepted.Contains(flag ? "true" : "false"),
            JsonValue v when v.TryGetValue<string>(out var text) => accepted.Contains(text),
            _ => false,
        };
    }

    private static ScoreResult ComputeScore(TemplateBlock block, JsonObject values, Dictionary<string, TemplateBlock> blocks)
    {
        var total = 0;
        foreach (var key in block.SumOf!)
        {
            var answer = values[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
            var option = answer is null || !blocks.TryGetValue(key, out var question)
                ? null
                : question.Options?.FirstOrDefault(o => o.Value == answer);
            if (option?.Score is not { } points)
            {
                return new ScoreResult(null, null, null);
            }

            total += points;
        }

        var level = block.Levels!.LastOrDefault(l => total >= l.Min);
        return new ScoreResult(total, level?.Code, level?.Label);
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            errors[key] = list = [];
        }

        list.Add(message);
    }

    /// <summary>Đọc dữ liệu phiếu đã lưu (JSON object); dữ liệu hỏng trả về object rỗng.</summary>
    public static JsonObject ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
