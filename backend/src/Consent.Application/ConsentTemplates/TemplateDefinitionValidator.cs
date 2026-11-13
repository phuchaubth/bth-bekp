using System.Text.RegularExpressions;

namespace Consent.Application.ConsentTemplates;

/// <summary>
/// Kiểm tra cấu trúc định nghĩa mẫu trước khi lưu/phát hành: kiểu khối hợp lệ, tên trường không trùng, lựa chọn đủ,
/// điều kiện hiển thị trỏ tới trường đứng trước, câu hỏi tính điểm có điểm cho mọi lựa chọn…
/// </summary>
public static partial class TemplateDefinitionValidator
{
    public const int MaxTextLength = 4000;
    public const int MaxLabelLength = 500;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Đọc và kiểm tra JSON. Trả về định nghĩa hợp lệ, hoặc danh sách lỗi (tiếng Việt).</summary>
    public static (TemplateDefinition? Definition, IReadOnlyList<string> Errors) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, ["Định nghĩa mẫu trống."]);
        }

        var definition = TemplateJson.TryDeserialize(json, out var error);
        if (definition is null)
        {
            return (null, [error ?? "Định nghĩa mẫu không hợp lệ."]);
        }

        var errors = Validate(definition);
        return errors.Count == 0 ? (definition, errors) : (null, errors);
    }

    public static IReadOnlyList<string> Validate(TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.Title))
        {
            errors.Add("Thiếu tiêu đề phiếu.");
        }

        if (definition.Sections is not { Count: > 0 })
        {
            errors.Add("Mẫu phải có ít nhất một phần.");
            return errors;
        }

        // Tên trường đã khai báo (theo thứ tự) → khối, để kiểm tra điều kiện và tổng điểm chỉ trỏ tới trường đứng trước.
        var declared = new Dictionary<string, TemplateBlock>(StringComparer.Ordinal);
        for (var s = 0; s < definition.Sections.Count; s++)
        {
            var section = definition.Sections[s];
            if (section?.Blocks is null)
            {
                errors.Add($"Phần {s + 1}: thiếu danh sách khối.");
                continue;
            }

            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var block = section.Blocks[b];
                var where = $"Phần {s + 1}, khối {b + 1}";
                if (block is null || !BlockTypes.All.Contains(block.Type ?? string.Empty))
                {
                    errors.Add($"{where}: kiểu khối không hợp lệ.");
                    continue;
                }

                ValidateBlock(block, where, declared, errors);
                if (BlockTypes.IsInput(block.Type!) && block.Key is not null && KeyPattern().IsMatch(block.Key))
                {
                    if (!declared.TryAdd(block.Key, block))
                    {
                        errors.Add($"{where}: tên trường \"{block.Key}\" bị trùng.");
                    }
                }
            }
        }

        ValidateSignatures(definition.Signatures, errors);
        return errors;
    }

    private static void ValidateSignatures(TemplateSignatures? signatures, List<string> errors)
    {
        if (signatures is null)
        {
            return;
        }

        if (signatures.Decision is not (null or TemplateSignatures.DecisionRequired or TemplateSignatures.DecisionNone))
        {
            errors.Add("Khối chữ ký: \"decision\" chỉ nhận \"required\" (bắt buộc chọn Đồng ý/Từ chối) hoặc \"none\" (chỉ ký xác nhận).");
        }

        foreach (var label in new[]
                 {
                     signatures.Patient, signatures.Representative, signatures.Hospital, signatures.HospitalHint,
                     signatures.DecisionLabel, signatures.AgreeLabel, signatures.RefuseLabel,
                 })
        {
            if (label is { Length: > MaxLabelLength })
            {
                errors.Add($"Khối chữ ký: nhãn quá dài (tối đa {MaxLabelLength} ký tự).");
                return;
            }
        }
    }

    private static void ValidateBlock(TemplateBlock block, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        switch (block.Type)
        {
            case BlockTypes.Paragraph:
                RequireText(block.Text, $"{where}: đoạn văn trống.", errors);
                return;
            case BlockTypes.List:
                if (block.Items is not { Count: > 0 } || block.Items.Any(string.IsNullOrWhiteSpace))
                {
                    errors.Add($"{where}: danh sách phải có ít nhất một dòng và không có dòng trống.");
                }

                return;
        }

        if (block.Key is null || !KeyPattern().IsMatch(block.Key))
        {
            errors.Add($"{where}: tên trường phải bắt đầu bằng chữ cái, chỉ gồm chữ không dấu, số, dấu gạch dưới (tối đa 50 ký tự).");
        }

        RequireText(block.Label, $"{where}: thiếu nhãn.", errors);
        if (block.MaxLength is <= 0 or > MaxTextLength)
        {
            errors.Add($"{where}: độ dài tối đa phải từ 1 đến {MaxTextLength}.");
        }

        ValidatePattern(block.Pattern, where, errors);
        if (block.Prefill is not null && !PrefillSources.All.Contains(block.Prefill))
        {
            errors.Add($"{where}: nguồn tự điền \"{block.Prefill}\" không được hỗ trợ.");
        }

        switch (block.Type)
        {
            case BlockTypes.Choice:
            case BlockTypes.MultiChoice:
                ValidateOptions(block, where, errors);
                break;
            case BlockTypes.Repeater:
                ValidateColumns(block, where, errors);
                break;
            case BlockTypes.Score:
                ValidateScore(block, where, declared, errors);
                break;
        }

        if (block.MinItems is < 0 || block.MaxItems is < 1 || (block.MinItems ?? 0) > (block.MaxItems ?? int.MaxValue))
        {
            errors.Add($"{where}: số mục tối thiểu/tối đa không hợp lệ.");
        }

        if (block.VisibleWhen is { } condition)
        {
            ValidateCondition(condition, where, declared, errors);
        }
    }

    private static void ValidateOptions(TemplateBlock block, string where, List<string> errors)
    {
        if (block.Options is not { Count: > 0 })
        {
            errors.Add($"{where}: phải có ít nhất một lựa chọn.");
            return;
        }

        if (block.Options.Any(o => o is null || string.IsNullOrWhiteSpace(o.Value) || string.IsNullOrWhiteSpace(o.Label)))
        {
            errors.Add($"{where}: mỗi lựa chọn phải có giá trị và nhãn.");
            return;
        }

        if (block.Options.Select(o => o.Value).Distinct(StringComparer.Ordinal).Count() != block.Options.Count)
        {
            errors.Add($"{where}: giá trị các lựa chọn bị trùng.");
        }

        var scored = block.Options.Count(o => o.Score is not null);
        if (scored != 0 && (scored != block.Options.Count || block.Type != BlockTypes.Choice))
        {
            errors.Add($"{where}: câu hỏi tính điểm phải là chọn một và có điểm cho mọi lựa chọn.");
        }
    }

    private static void ValidateColumns(TemplateBlock block, string where, List<string> errors)
    {
        if (block.Columns is not { Count: > 0 })
        {
            errors.Add($"{where}: bảng phải có ít nhất một cột.");
            return;
        }

        foreach (var column in block.Columns)
        {
            if (column is null || !KeyPattern().IsMatch(column.Key ?? string.Empty) || string.IsNullOrWhiteSpace(column.Label))
            {
                errors.Add($"{where}: mỗi cột phải có tên trường hợp lệ và nhãn.");
                return;
            }

            ValidatePattern(column.Pattern, where, errors);
        }

        if (block.Columns.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != block.Columns.Count)
        {
            errors.Add($"{where}: tên cột bị trùng.");
        }
    }

    private static void ValidateScore(TemplateBlock block, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (block.SumOf is not { Count: > 0 })
        {
            errors.Add($"{where}: trường tính điểm phải liệt kê các câu hỏi được cộng.");
        }
        else
        {
            foreach (var key in block.SumOf)
            {
                if (!declared.TryGetValue(key, out var question) || question.Options is not { Count: > 0 }
                    || question.Options.Any(o => o.Score is null))
                {
                    errors.Add($"{where}: \"{key}\" phải là câu hỏi tính điểm đứng trước.");
                }
            }
        }

        if (block.Levels is not { Count: > 0 })
        {
            errors.Add($"{where}: phải có ít nhất một mức.");
            return;
        }

        var ordered = block.Levels.Zip(block.Levels.Skip(1)).All(pair => pair.First.Min < pair.Second.Min);
        var codes = block.Levels.Select(l => l.Code).Distinct(StringComparer.Ordinal).Count();
        if (!ordered || codes != block.Levels.Count || block.Levels.Any(l => string.IsNullOrWhiteSpace(l.Code) || string.IsNullOrWhiteSpace(l.Label)))
        {
            errors.Add($"{where}: các mức phải có mã, nhãn, không trùng mã và tăng dần theo điểm tối thiểu.");
        }
    }

    private static void ValidateCondition(TemplateCondition condition, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (!declared.TryGetValue(condition.Field ?? string.Empty, out var source))
        {
            errors.Add($"{where}: điều kiện hiển thị phải dựa trên một trường đứng trước.");
            return;
        }

        var hasIn = condition.In is { Count: > 0 };
        var hasLevel = !string.IsNullOrWhiteSpace(condition.MinLevel);
        if (hasIn == hasLevel)
        {
            errors.Add($"{where}: điều kiện hiển thị cần đúng một trong hai: danh sách giá trị hoặc mức tối thiểu.");
            return;
        }

        if (hasLevel && (source.Type != BlockTypes.Score || source.Levels?.Any(l => l.Code == condition.MinLevel) != true))
        {
            errors.Add($"{where}: mức \"{condition.MinLevel}\" không có trong trường điểm \"{condition.Field}\".");
        }

        if (hasIn && source.Type is BlockTypes.Choice or BlockTypes.MultiChoice
            && condition.In!.Any(v => source.Options?.Any(o => o.Value == v) != true))
        {
            errors.Add($"{where}: giá trị điều kiện không có trong lựa chọn của \"{condition.Field}\".");
        }
    }

    private static void ValidatePattern(string? pattern, string where, List<string> errors)
    {
        if (pattern is null)
        {
            return;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException)
        {
            errors.Add($"{where}: biểu thức kiểm tra định dạng không hợp lệ.");
        }
    }

    private static void RequireText(string? value, string message, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(message);
        }
        else if (value.Length > MaxTextLength)
        {
            errors.Add($"{message.Split(':')[0]}: nội dung quá dài (tối đa {MaxTextLength} ký tự).");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,49}$")]
    private static partial Regex KeyPattern();
}
