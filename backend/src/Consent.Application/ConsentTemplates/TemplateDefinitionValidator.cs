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

        ValidateDocumentControl(definition.DocumentControl, errors);
        ValidatePatientLines(definition.PatientLines, declared, errors);
        ValidateSignatures(definition.Signatures, errors);
        ValidateSectionSigners(definition, errors);
        if (definition.Footnote is { Length: > TemplateDefinition.MaxFootnoteLength })
        {
            errors.Add($"Ghi chú cuối phiếu (\"footnote\"): tối đa {TemplateDefinition.MaxFootnoteLength} ký tự.");
        }

        return errors;
    }

    /// <summary>
    /// Ô ký in cuối phần (<see cref="TemplateSection.Signer"/>): người bệnh, xác nhận cuối hoặc một bước xác nhận trung gian của
    /// mẫu; mỗi người ký in ở một phần.
    /// </summary>
    private static void ValidateSectionSigners(TemplateDefinition definition, List<string> errors)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { TemplateSection.PatientSigner, TemplateSection.HospitalSigner };
        known.UnionWith(definition.Signatures?.Steps?.Select(step => step.Key).OfType<string>() ?? []);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        for (var s = 0; s < definition.Sections.Count; s++)
        {
            if (definition.Sections[s]?.Signer is not { } signer)
            {
                continue;
            }

            if (!known.Contains(signer))
            {
                errors.Add($"Phần {s + 1}: người ký cuối phần (\"signer\") chỉ nhận \"patient\", \"hospital\" hoặc mã một bước xác nhận của mẫu.");
            }
            else if (!placed.Add(signer))
            {
                errors.Add($"Phần {s + 1}: người ký \"{signer}\" đã in ở cuối một phần khác.");
            }
        }
    }

    private static void ValidateDocumentControl(TemplateDocumentControl? control, List<string> errors)
    {
        if (control is not null
            && new[]
                {
                    control.Code, control.IssueNo, control.EffectiveDate, control.CodeLabel, control.IssueNoLabel, control.EffectiveDateLabel,
                }
                .Any(v => v is { Length: > TemplateDocumentControl.MaxLength }))
        {
            errors.Add(
                $"Mã tài liệu (\"documentControl\"): mã số, lần ban hành, ngày hiệu lực và các nhãn mỗi mục tối đa {TemplateDocumentControl.MaxLength} ký tự.");
        }

        if (control is { UnitInHead: true, TitleInHead: false })
        {
            errors.Add("Mã tài liệu (\"documentControl\"): \"unitInHead\" (tên đơn vị trên logo) chỉ dùng kèm \"titleInHead\".");
        }
    }

    /// <summary>Dòng thông tin người bệnh: mỗi mục là đúng một thông tin hồ sơ hoặc một trường "nhãn: giá trị" của mẫu.</summary>
    private static void ValidatePatientLines(
        IReadOnlyList<IReadOnlyList<PatientLineItem>>? lines, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (lines is null)
        {
            return;
        }

        if (lines.Count is 0 or > PatientLineSources.MaxLines)
        {
            errors.Add($"Dòng thông tin người bệnh (\"patientLines\"): từ 1 đến {PatientLineSources.MaxLines} dòng.");
            return;
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var l = 0; l < lines.Count; l++)
        {
            var where = $"Dòng thông tin người bệnh {l + 1}";
            if (lines[l] is not { Count: > 0 and <= PatientLineSources.MaxItemsPerLine } items)
            {
                errors.Add($"{where}: có 1 hoặc {PatientLineSources.MaxItemsPerLine} mục.");
                continue;
            }

            foreach (var item in items)
            {
                ValidatePatientLineItem(item, where, declared, fields, errors);
            }
        }
    }

    private static void ValidatePatientLineItem(
        PatientLineItem? item, string where, Dictionary<string, TemplateBlock> declared, HashSet<string> fields, List<string> errors)
    {
        if (item is null || (item.Patient is null) == (item.Field is null))
        {
            errors.Add($"{where}: mỗi mục ghi đúng một trong hai: \"patient\" (thông tin trong hồ sơ) hoặc \"field\" (trường của phiếu).");
            return;
        }

        if (item.Patient is { } source && !PatientLineSources.DefaultLabels.ContainsKey(source))
        {
            errors.Add($"{where}: \"{source}\" không in được (chỉ có: {string.Join(", ", PatientLineSources.DefaultLabels.Keys)}).");
        }

        TemplateBlock? field = null;
        if (item.Field is { } key)
        {
            if (!declared.TryGetValue(key, out field) || !(BlockTypes.CanShareLine(field.Type) || IsChoice(field.Type)))
            {
                errors.Add($"{where}: \"{key}\" phải là trường văn bản, ngày, ngày giờ, trường tự tính hoặc câu hỏi chọn của mẫu.");
                field = null;
            }
            else if (!fields.Add(key))
            {
                errors.Add($"{where}: trường \"{key}\" đã in ở dòng khác.");
            }
        }

        if (item.Label is { Length: > MaxLabelLength })
        {
            errors.Add($"{where}: nhãn tối đa {MaxLabelLength} ký tự.");
        }

        ValidatePatientLineOther(item, field, where, declared, fields, errors);
        if (item.Options is not { } options)
        {
            return;
        }

        if (item.Patient is not null)
        {
            if (item.Patient != PatientLineSources.Gender || options.Count != 2 || options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 100))
            {
                errors.Add($"{where}: \"options\" của thông tin hồ sơ chỉ dùng cho giới tính, gồm đúng hai nhãn (Nam, Nữ), mỗi nhãn tối đa 100 ký tự.");
            }
        }
        else if (field is not null
                 && (field.Type != BlockTypes.Text || options.Count is < 2 or > PatientLineItem.MaxFieldOptions
                     || options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 100)))
        {
            errors.Add(
                $"{where}: \"options\" của trường chỉ dùng cho trường văn bản, từ 2 đến {PatientLineItem.MaxFieldOptions} nhãn (nhãn cuối là \"Khác\"), mỗi nhãn tối đa 100 ký tự.");
        }
    }

    /// <summary><c>other</c>: trường văn bản in tiếp sau các ô của một câu hỏi chọn (ví dụ "Khác (Other): …").</summary>
    private static void ValidatePatientLineOther(
        PatientLineItem item, TemplateBlock? field, string where, Dictionary<string, TemplateBlock> declared, HashSet<string> fields, List<string> errors)
    {
        if (item.Other is not { } other)
        {
            return;
        }

        if (field is null || !IsChoice(field.Type))
        {
            errors.Add($"{where}: \"other\" chỉ dùng kèm một câu hỏi chọn của mẫu (\"field\").");
        }
        else if (!declared.TryGetValue(other, out var block) || block.Type != BlockTypes.Text)
        {
            errors.Add($"{where}: \"other\" phải là trường văn bản của mẫu.");
        }
        else if (!fields.Add(other))
        {
            errors.Add($"{where}: trường \"{other}\" đã in ở dòng khác.");
        }
    }

    private static bool IsChoice(string type) => type is BlockTypes.Choice or BlockTypes.MultiChoice;

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
                     signatures.Patient, signatures.Representative, signatures.Hospital, signatures.HospitalHint, signatures.PatientHint,
                     signatures.DecisionLabel, signatures.AgreeLabel, signatures.RefuseLabel,
                 })
        {
            if (label is { Length: > MaxLabelLength })
            {
                errors.Add($"Khối chữ ký: nhãn quá dài (tối đa {MaxLabelLength} ký tự).");
                return;
            }
        }

        ValidateSteps(signatures.Steps, errors);
        ValidateStatement(signatures.Statement, errors);
    }

    private static void ValidateStatement(TemplateStatement? statement, List<string> errors)
    {
        if (statement is null)
        {
            return;
        }

        if (statement.Label is { Length: > MaxLabelLength })
        {
            errors.Add($"Khối chữ ký, lời xác nhận: nhãn tối đa {MaxLabelLength} ký tự.");
        }

        var suggestions = statement.Suggestions ?? [];
        if (suggestions.Count > TemplateStatement.MaxSuggestions)
        {
            errors.Add($"Khối chữ ký, lời xác nhận: tối đa {TemplateStatement.MaxSuggestions} câu gợi ý.");
        }

        if (suggestions.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > TemplateStatement.MaxLength))
        {
            errors.Add($"Khối chữ ký, lời xác nhận: câu gợi ý không được trống, tối đa {TemplateStatement.MaxLength} ký tự.");
        }
    }

    private static void ValidateSteps(IReadOnlyList<TemplateConfirmationStep>? steps, List<string> errors)
    {
        if (steps is null)
        {
            return;
        }

        if (steps.Count > TemplateSignatures.MaxSteps)
        {
            errors.Add($"Khối chữ ký: tối đa {TemplateSignatures.MaxSteps} bước xác nhận.");
            return;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var where = $"Khối chữ ký, bước {i + 1}";
            if (step is null)
            {
                errors.Add($"{where}: trống.");
                continue;
            }

            if (!KeyPattern().IsMatch(step.Key ?? string.Empty))
            {
                errors.Add($"{where}: mã bước phải bắt đầu bằng chữ cái, chỉ gồm chữ không dấu, số, dấu gạch dưới (tối đa 50 ký tự).");
            }

            if (!RoleCodePattern().IsMatch(step.RoleCode ?? string.Empty))
            {
                errors.Add($"{where}: mã vai trò không hợp lệ (ví dụ NURSE).");
            }

            if (string.IsNullOrWhiteSpace(step.Label) || step.Label.Length > MaxLabelLength
                || step.Hint is { Length: > MaxLabelLength })
            {
                errors.Add($"{where}: phải có nhãn, nhãn và ghi chú tối đa {MaxLabelLength} ký tự.");
            }
        }

        if (steps.Where(s => s?.Key is not null).Select(s => s.Key).Distinct(StringComparer.Ordinal).Count()
            != steps.Count(s => s?.Key is not null))
        {
            errors.Add("Khối chữ ký: mã bước xác nhận bị trùng.");
        }
    }

    /// <summary>Số lựa chọn tối đa của một câu hỏi in thành hàng bảng (mỗi lựa chọn một ô).</summary>
    private const int MaxRowOptions = 4;

    private static void ValidateBlock(TemplateBlock block, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (block.SameLine && !BlockTypes.CanShareLine(block.Type))
        {
            errors.Add($"{where}: \"sameLine\" (in cùng dòng với trường đứng trước) chỉ dùng cho trường văn bản, ngày, ngày giờ hoặc trường tự tính.");
        }

        if (block.PrintOptionsOnly && block.Type is not (BlockTypes.Choice or BlockTypes.MultiChoice))
        {
            errors.Add($"{where}: \"printOptionsOnly\" (chỉ in các ô lựa chọn) chỉ dùng cho câu hỏi chọn một hoặc chọn nhiều.");
        }

        if (block.Layout == TemplateBlock.LayoutRow
            && (block.Type != BlockTypes.Choice || block.PrintOptionsOnly || block.Options is not { Count: >= 2 and <= MaxRowOptions }))
        {
            errors.Add($"{where}: \"layout\": \"row\" (một hàng của bảng) chỉ dùng cho câu hỏi chọn một có từ 2 đến {MaxRowOptions} lựa chọn, không kèm \"printOptionsOnly\".");
        }

        if (block.Box && block.Type != BlockTypes.List)
        {
            errors.Add($"{where}: \"box\" (in trong khung) chỉ dùng cho danh sách cố định.");
        }

        if (block.Heading && block.Type != BlockTypes.Paragraph)
        {
            errors.Add($"{where}: \"heading\" (in thành tiêu đề mục) chỉ dùng cho đoạn văn.");
        }

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
                ValidateValueFrom(block, where, declared, errors);
                break;
            case BlockTypes.Repeater:
                ValidateColumns(block, where, errors);
                break;
            case BlockTypes.Score:
                ValidateScore(block, where, declared, errors);
                break;
            case BlockTypes.Computed:
                ValidateComputed(block, where, declared, errors);
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

    private static void ValidateValueFrom(TemplateBlock block, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (block.ValueFrom is null)
        {
            return;
        }

        if (block.Type != BlockTypes.Choice)
        {
            errors.Add($"{where}: chỉ câu hỏi chọn một mới tự chọn theo trường khác.");
        }

        if (!declared.TryGetValue(block.ValueFrom, out var source) || source.Type is not (BlockTypes.Computed or BlockTypes.Text))
        {
            errors.Add($"{where}: \"{block.ValueFrom}\" phải là trường văn bản hoặc trường tự tính đứng trước.");
        }

        if (block.Options?.Any(o => o is null || (o.Min is null && o.Max is null) || o.Min >= o.Max) == true)
        {
            errors.Add($"{where}: mỗi lựa chọn phải có khoảng giá trị \"min\" và/hoặc \"max\" (min < max).");
        }
    }

    private static void ValidateComputed(TemplateBlock block, string where, Dictionary<string, TemplateBlock> declared, List<string> errors)
    {
        if (block.Formula is null || !ComputedFormulas.InputCounts.TryGetValue(block.Formula, out var count))
        {
            errors.Add($"{where}: công thức \"{block.Formula}\" không được hỗ trợ (hiện có: {string.Join(", ", ComputedFormulas.InputCounts.Keys)}).");
            return;
        }

        if (block.Inputs is null || block.Inputs.Count != count)
        {
            errors.Add($"{where}: công thức \"{block.Formula}\" cần đúng {count} trường đầu vào.");
            return;
        }

        foreach (var key in block.Inputs)
        {
            if (!declared.TryGetValue(key, out var input) || input.Type != BlockTypes.Text)
            {
                errors.Add($"{where}: \"{key}\" phải là trường văn bản đứng trước.");
            }
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

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,49}$")]
    private static partial Regex RoleCodePattern();
}
