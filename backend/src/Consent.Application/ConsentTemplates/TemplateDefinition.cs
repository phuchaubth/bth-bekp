using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Consent.Application.ConsentTemplates;

/// <summary>
/// Nội dung một phiên bản mẫu phiếu (lưu dạng JSON). Khối "Thông tin người bệnh", số phiếu và chữ ký do hệ thống
/// tự thêm; định nghĩa chỉ mô tả phần riêng của từng mẫu.
/// </summary>
public sealed record TemplateDefinition(
    string Title,
    string? Subtitle,
    IReadOnlyList<TemplateSection> Sections,
    TemplateSignatures? Signatures = null)
{
    /// <summary>Mọi khối nhập liệu (có <see cref="TemplateBlock.Key"/>), theo thứ tự xuất hiện.</summary>
    public IEnumerable<TemplateBlock> InputBlocks =>
        Sections.SelectMany(s => s.Blocks).Where(b => BlockTypes.IsInput(b.Type));
}

public sealed record TemplateSection(string? Title, IReadOnlyList<TemplateBlock> Blocks);

/// <summary>Một khối của mẫu: đoạn văn cố định, danh sách cố định, hoặc trường nhập liệu (xem <see cref="BlockTypes"/>).</summary>
public sealed record TemplateBlock
{
    public required string Type { get; init; }

    /// <summary>Tên trường (bắt buộc với khối nhập liệu), ví dụ <c>examType</c>.</summary>
    public string? Key { get; init; }

    public string? Label { get; init; }

    /// <summary>Nội dung đoạn văn (khối <c>paragraph</c>).</summary>
    public string? Text { get; init; }

    /// <summary>Các dòng của danh sách cố định (khối <c>list</c>).</summary>
    public IReadOnlyList<string>? Items { get; init; }

    public bool Required { get; init; }

    /// <summary>Chọn nhiều: phải đánh dấu đủ mọi mục.</summary>
    public bool RequireAll { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    public string? PatternMessage { get; init; }

    /// <summary>Tự điền từ hồ sơ khi lập phiếu, ví dụ <c>patient.healthInsuranceNumber</c> (xem <see cref="PrefillSources"/>).</summary>
    public string? Prefill { get; init; }

    /// <summary><c>inline</c>: các lựa chọn trên một dòng; <c>ordered</c>: danh sách có đánh số.</summary>
    public string? Layout { get; init; }

    public IReadOnlyList<TemplateOption>? Options { get; init; }

    /// <summary>Các cột của bảng nhiều dòng (khối <c>repeater</c>).</summary>
    public IReadOnlyList<TemplateColumn>? Columns { get; init; }

    public TemplateCondition? VisibleWhen { get; init; }

    /// <summary>Khối <c>score</c>: các câu hỏi tính điểm được cộng.</summary>
    public IReadOnlyList<string>? SumOf { get; init; }

    /// <summary>Khối <c>score</c>: các mức theo tổng điểm, tăng dần theo <see cref="ScoreLevel.Min"/>.</summary>
    public IReadOnlyList<ScoreLevel>? Levels { get; init; }

    /// <summary>
    /// Câu hỏi chọn một tự chọn theo giá trị số của trường đứng trước (ví dụ mức BMI theo BMI tự tính): chọn lựa chọn có
    /// khoảng <c>min</c>/<c>max</c> chứa giá trị đó. Trường nguồn chưa có giá trị thì người dùng chọn tay.
    /// </summary>
    public string? ValueFrom { get; init; }

    /// <summary>Khối <c>computed</c>: công thức (xem <see cref="ComputedFormulas"/>, ví dụ <c>bmi</c>).</summary>
    public string? Formula { get; init; }

    /// <summary>Khối <c>computed</c>: các trường đầu vào theo thứ tự của công thức (bmi: cân nặng kg, chiều cao cm).</summary>
    public IReadOnlyList<string>? Inputs { get; init; }
}

/// <summary>Lựa chọn; <see cref="Score"/> có giá trị khi là câu hỏi tính điểm.</summary>
/// <param name="Min">Khi câu hỏi có <see cref="TemplateBlock.ValueFrom"/>: giá trị nguồn ≥ Min (bỏ trống: không giới hạn dưới).</param>
/// <param name="Max">Khi câu hỏi có <see cref="TemplateBlock.ValueFrom"/>: giá trị nguồn &lt; Max (bỏ trống: không giới hạn trên).</param>
public sealed record TemplateOption(string Value, string Label, int? Score = null, decimal? Min = null, decimal? Max = null);

public sealed record TemplateColumn(
    string Key, string Label, bool Required = false, int? MaxLength = null, string? Pattern = null, string? PatternMessage = null);

/// <summary>
/// Điều kiện hiển thị: giá trị trường <see cref="Field"/> thuộc <see cref="In"/> (với chọn nhiều: có ít nhất một),
/// hoặc trường điểm <see cref="Field"/> đạt tối thiểu mức <see cref="MinLevel"/>. Trường bị ẩn thì không bắt buộc và
/// không được lưu.
/// </summary>
public sealed record TemplateCondition(string Field, IReadOnlyList<string>? In = null, string? MinLevel = null);

public sealed record ScoreLevel(string Code, string Label, int Min);

/// <summary>
/// Khối chữ ký: nhãn (bỏ trống thì dùng nhãn mặc định) và cách ghi nhận ý kiến của người bệnh khi ký.
/// <see cref="Decision"/> = <c>none</c>: người bệnh chỉ ký xác nhận (phiếu khai thông tin, phiếu nhận định), không hỏi
/// Đồng ý/Từ chối. Mặc định (<c>required</c>): bắt buộc chọn; <see cref="DecisionLabel"/>, <see cref="AgreeLabel"/>,
/// <see cref="RefuseLabel"/> thay câu hỏi và nhãn mặc định ("Ý kiến của người bệnh", "Đồng ý", "Từ chối").
/// <see cref="Steps"/>: các bước xác nhận trung gian theo vai trò, làm lần lượt sau khi người bệnh ký và trước bước xác
/// nhận cuối (vai trò cấu hình trên mẫu, nhãn <see cref="Hospital"/>).
/// </summary>
public sealed record TemplateSignatures(
    string? Patient = null,
    string? Representative = null,
    string? Hospital = null,
    string? HospitalHint = null,
    string? Decision = null,
    string? DecisionLabel = null,
    string? AgreeLabel = null,
    string? RefuseLabel = null,
    IReadOnlyList<TemplateConfirmationStep>? Steps = null)
{
    public const int MaxSteps = 5;

    public const string DecisionRequired = "required";
    public const string DecisionNone = "none";

    public bool RequiresDecision => !string.Equals(Decision, DecisionNone, StringComparison.Ordinal);
}

/// <summary>
/// Một bước xác nhận trung gian: nhân viên có vai trò <see cref="RoleCode"/> xác nhận bằng tài khoản. <see cref="Label"/>
/// là nhãn ô chữ ký trên PDF, ví dụ "Điều dưỡng thực hiện (Nurse in charge)".
/// </summary>
public sealed record TemplateConfirmationStep(string Key, string RoleCode, string Label, string? Hint = null);

public static class BlockTypes
{
    public const string Paragraph = "paragraph";
    public const string List = "list";
    public const string Text = "text";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Choice = "choice";
    public const string MultiChoice = "multiChoice";
    public const string Confirm = "confirm";
    public const string Repeater = "repeater";
    public const string Score = "score";

    /// <summary>Trường tự tính theo công thức từ các trường khác (ví dụ BMI từ cân nặng và chiều cao); người dùng không nhập.</summary>
    public const string Computed = "computed";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            [Paragraph, List, Text, Date, DateTime, Choice, MultiChoice, Confirm, Repeater, Score, Computed], StringComparer.Ordinal);

    /// <summary>Khối có tên trường (dữ liệu nhập hoặc giá trị tự tính).</summary>
    public static bool IsInput(string type) => type is not (Paragraph or List);
}

/// <summary>Nguồn tự điền khi lập phiếu.</summary>
public static class PrefillSources
{
    public const string HealthInsuranceNumber = "patient.healthInsuranceNumber";
    public const string PhoneNumber = "patient.phoneNumber";
    public const string PatientCode = "patient.patientCode";
    public const string PermanentAddress = "patient.permanentAddress";
    public const string CurrentAddress = "patient.currentAddress";
    public const string Ethnicity = "patient.ethnicity";
    public const string Occupation = "patient.occupation";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            [HealthInsuranceNumber, PhoneNumber, PatientCode, PermanentAddress, CurrentAddress, Ethnicity, Occupation],
            StringComparer.Ordinal);
}

/// <summary>Đọc/ghi định nghĩa mẫu và dữ liệu phiếu dạng JSON (camelCase).</summary>
public static class TemplateJson
{
    private static readonly HashSet<Type> DefinitionTypes =
    [
        typeof(TemplateDefinition), typeof(TemplateSection), typeof(TemplateBlock), typeof(TemplateOption),
        typeof(TemplateColumn), typeof(TemplateCondition), typeof(ScoreLevel), typeof(TemplateSignatures), typeof(TemplateConfirmationStep),
    ];

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = false,
    };

    public static string Serialize(TemplateDefinition definition) => JsonSerializer.Serialize(definition, Options);

    /// <summary>
    /// Bộ điều chỉnh cho JSON của API: định nghĩa mẫu trả về bỏ thuộc tính trống (<c>null</c>, <c>false</c>), gọn như
    /// bản đang lưu, để màn hình soạn JSON hiển thị đúng định nghĩa và client chỉ cần xử lý "không có thuộc tính".
    /// Giá trị số luôn được giữ (ví dụ điểm 0 của một lựa chọn, mức điểm tối thiểu 0).
    /// </summary>
    public static void OmitEmptyDefinitionMembers(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !DefinitionTypes.Contains(typeInfo.Type))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            property.ShouldSerialize = static (_, value) => value is not (null or false);
        }
    }

    /// <summary>Đọc định nghĩa; JSON sai cú pháp hoặc sai kiểu dữ liệu trả về <c>null</c> kèm thông báo.</summary>
    public static TemplateDefinition? TryDeserialize(string json, out string? error)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<TemplateDefinition>(json, Options);
            error = definition is null ? "Định nghĩa mẫu trống." : null;
            return definition;
        }
        catch (JsonException ex)
        {
            error = $"Định nghĩa mẫu không đúng cấu trúc JSON (dòng {ex.LineNumber + 1}).";
            return null;
        }
    }
}
