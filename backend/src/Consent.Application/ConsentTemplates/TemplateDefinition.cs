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
    /// <summary>Không in dòng "Khoa/phòng: …" dưới tên bệnh viện ở đầu phiếu (bản giấy của mẫu không có dòng này).</summary>
    public bool HideDepartment { get; init; }

    /// <summary>
    /// Mẫu song ngữ: phần hệ thống tự in cũng kèm tiếng Anh: mục "I. Thông tin người bệnh" ("Họ và tên (Full name)"), dòng
    /// ngày tháng ("ngày (day) … tháng (month) … năm (year)") và ghi chú dưới tên ô ký ("(Signature and full name)").
    /// </summary>
    public bool Bilingual { get; init; }

    /// <summary>
    /// Mã tài liệu của bệnh viện (mã số, lần ban hành, ngày hiệu lực). Có thì đầu phiếu in như bản giấy: logo bên trái, quốc
    /// hiệu ở giữa, ô mã tài liệu bên phải; không in tên đơn vị (trừ khi chưa tải logo), dòng khoa và dòng "MS … Số phiếu"
    /// (số phiếu vẫn in ở chân trang).
    /// </summary>
    public TemplateDocumentControl? DocumentControl { get; init; }

    /// <summary>
    /// Các dòng thông tin người bệnh in như bản giấy, thay cho mục "I. Thông tin người bệnh": mỗi dòng 1–2 mục, mỗi mục là một
    /// thông tin trong hồ sơ (<see cref="PatientLineItem.Patient"/>) hoặc một trường của phiếu (<see cref="PatientLineItem.Field"/>;
    /// trường đó không in lại ở thân phiếu).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PatientLineItem>>? PatientLines { get; init; }

    /// <summary>
    /// Đánh số 1, 2, 3… các dòng thông tin người bệnh (<see cref="PatientLines"/>) như bản giấy TTNB (QT-KNGT/BM03); dòng có
    /// mục đầu là <see cref="PatientLineItem.Indent"/> là dòng phụ của dòng trên, không đánh số.
    /// </summary>
    public bool NumberedPatientLines { get; init; }

    /// <summary>
    /// Ghi chú in cuối phiếu, sau khối chữ ký, chữ nhỏ nghiêng, có đường kẻ phía trên, như phần "Hướng dẫn thực hiện phiếu" của
    /// bản giấy QĐ-VHTT-BM02 (quyết định 43); "\n" là xuống dòng. Màn hình nhập liệu không hiện.
    /// </summary>
    public string? Footnote { get; init; }

    public const int MaxFootnoteLength = 2000;

    /// <summary>
    /// Mọi khối nhập liệu (có <see cref="TemplateBlock.Key"/>), theo thứ tự xuất hiện. Chỉ để tính toán: không ghi vào JSON
    /// (trước đây bị ghi thành bản sao "inputBlocks", sửa ở bản sao đó không có tác dụng).
    /// </summary>
    [JsonIgnore]
    public IEnumerable<TemplateBlock> InputBlocks =>
        Sections.SelectMany(s => s.Blocks).Where(b => BlockTypes.IsInput(b.Type));
}

public sealed record TemplateSection(string? Title, IReadOnlyList<TemplateBlock> Blocks)
{
    /// <summary>
    /// PDF in tên phần trong dải nền màu, canh giữa, chữ hoa đậm (dòng đầu), như các phần của bản giấy QT-KNGT/BM10 (quyết định
    /// 43); các dòng sau (tiếng Anh, ghi chú) chữ thường. Màn hình nhập liệu không đổi.
    /// </summary>
    public bool Banner { get; init; }

    /// <summary>PDF bắt đầu phần này ở trang mới, như bản giấy mỗi phần một trang. Màn hình nhập liệu không đổi.</summary>
    public bool NewPage { get; init; }

    /// <summary>
    /// PDF in ô ký của một người ngay cuối phần này (ngày tháng, tên ô ký, chữ ký) thay vì ở cuối phiếu, như bản giấy BM10 mỗi
    /// phần có người ký riêng: <c>patient</c> (người bệnh/người đại diện), <c>hospital</c> (xác nhận cuối), hoặc khóa của một
    /// bước xác nhận trung gian (<see cref="TemplateConfirmationStep.Key"/>).
    /// </summary>
    public string? Signer { get; init; }

    public const string PatientSigner = "patient";
    public const string HospitalSigner = "hospital";
}

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

    /// <summary>
    /// <c>inline</c>: các lựa chọn trên một dòng; <c>ordered</c>: danh sách có đánh số; <c>row</c> (câu hỏi chọn một): PDF in
    /// thành một hàng của bảng kẻ ô (câu hỏi, rồi mỗi lựa chọn một ô), các câu <c>row</c> liền nhau cùng một bảng, như bảng
    /// "Có/Không" của bản giấy BM10 (quyết định 43); màn hình nhập liệu in các lựa chọn trên một dòng.
    /// </summary>
    public string? Layout { get; init; }

    public const string LayoutInline = "inline";
    public const string LayoutOrdered = "ordered";
    public const string LayoutRow = "row";

    /// <summary>
    /// Danh sách cố định: PDF in trong khung nền xám, <see cref="Label"/> là tiêu đề khung (canh giữa, đậm nghiêng), các mục
    /// chữ nghiêng, như khung "Xin Quý người bệnh và thân nhân lưu ý" của bản giấy QĐ-VHTT-BM02 (quyết định 43). Màn hình nhập
    /// liệu hiện danh sách trong khung có tiêu đề.
    /// </summary>
    public bool Box { get; init; }

    /// <summary>
    /// Đoạn văn: PDF in thành tiêu đề mục (đậm, canh giữa, chữ hoa), như "A - NGUY CƠ TÉ NGÃ (FALL RISK)" của bản giấy BM10
    /// (quyết định 43). Màn hình nhập liệu in như đoạn văn.
    /// </summary>
    public bool Heading { get; init; }

    /// <summary>
    /// PDF in trường này tiếp trên dòng của trường đứng trước, ví dụ "Trình độ văn hóa: … Nghề nghiệp: …" (chỉ trường dạng
    /// "nhãn: giá trị", xem <see cref="BlockTypes.CanShareLine"/>). Màn hình nhập liệu không đổi.
    /// </summary>
    public bool SameLine { get; init; }

    /// <summary>
    /// Câu hỏi chọn: PDF chỉ in các ô lựa chọn (chữ đậm, một dòng riêng), không in câu hỏi, vì đoạn văn đứng trước đã là câu
    /// hỏi (như bản giấy). Màn hình nhập liệu vẫn hiện câu hỏi.
    /// </summary>
    public bool PrintOptionsOnly { get; init; }

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
/// <param name="HospitalFirst">PDF in các ô ký phía bệnh viện trước (bên trái), ô của người bệnh/người đại diện sau cùng.</param>
/// <param name="PlainTitles">PDF in tên ô ký đúng như đã ghi (không viết hoa toàn bộ, không in đậm), như bản giấy.</param>
/// <param name="PatientHint">
/// Ghi chú dưới tên ô ký của người bệnh/người đại diện trên PDF (bỏ trống: "(Ký, ghi rõ họ tên)"). Ở mọi ghi chú dưới tên ô
/// ký, dòng bắt đầu bằng dấu "!" in màu đỏ (dấu "!" không in), như ghi chú "đã đối chiếu…" của bản giấy TTNB.
/// </param>
/// <param name="CenterDate">Dòng "địa danh, ngày … tháng … năm …" canh giữa, chữ đứng (mặc định canh phải, chữ nghiêng).</param>
/// <param name="PaperHints">
/// Ghi chú dưới tên ô ký in gần bằng cỡ chữ tên ô ký (cỡ 11) như bản giấy, và giữ nguyên sau khi nhân viên xác nhận (trạng
/// thái "Đã ký, xác nhận bằng tài khoản" in dưới họ tên). Mặc định ghi chú in cỡ 10 và được thay bằng trạng thái xác nhận.
/// </param>
/// <param name="BoldTitles">
/// PDF in tên ô ký đậm nhưng giữ chữ thường như đã ghi (mặc định viết hoa toàn bộ), như bản giấy QĐ-VHTT-BM02 và QT-KNGT/BM10
/// (quyết định 43).
/// </param>
public sealed record TemplateSignatures(
    string? Patient = null,
    string? Representative = null,
    string? Hospital = null,
    string? HospitalHint = null,
    string? Decision = null,
    string? DecisionLabel = null,
    string? AgreeLabel = null,
    string? RefuseLabel = null,
    IReadOnlyList<TemplateConfirmationStep>? Steps = null,
    TemplateStatement? Statement = null,
    bool HospitalFirst = false,
    bool PlainTitles = false,
    string? PatientHint = null,
    bool CenterDate = false,
    bool PaperHints = false,
    bool BoldTitles = false)
{
    public const int MaxSteps = 5;

    public const string DecisionRequired = "required";
    public const string DecisionNone = "none";

    public bool RequiresDecision => !string.Equals(Decision, DecisionNone, StringComparison.Ordinal);
}

/// <summary>
/// Ô lời xác nhận của người ký (người bệnh/người đại diện) ở khung ký, ví dụ "Tôi đã nghe, hiểu và đồng ý", kèm các câu gợi
/// ý bấm chọn nhanh. Không khai báo thì không có ô này.
/// </summary>
public sealed record TemplateStatement(string? Label = null, bool Required = false, IReadOnlyList<string>? Suggestions = null)
{
    public const int MaxSuggestions = 8;

    /// <summary>Độ dài tối đa của lời xác nhận (trùng với cột lưu chữ ký).</summary>
    public const int MaxLength = 300;

    public const string DefaultLabel = "Lời xác nhận của người ký";
}

/// <summary>
/// Một bước xác nhận trung gian: nhân viên có vai trò <see cref="RoleCode"/> xác nhận bằng tài khoản. <see cref="Label"/>
/// là nhãn ô chữ ký trên PDF, ví dụ "Điều dưỡng thực hiện (Nurse in charge)".
/// </summary>
public sealed record TemplateConfirmationStep(string Key, string RoleCode, string Label, string? Hint = null);

/// <summary>
/// Mã tài liệu in ở góc phải đầu phiếu, ví dụ mã số "QT-KNGT/BM02", lần ban hành "09"; mục để trống thì chỉ in nhãn. Nhãn
/// bỏ trống thì dùng nhãn của bản giấy BM02 ("Mã số (Code No)", "Lần ban hành (Issue No)", "Ngày hiệu lực (Effective Date)").
/// </summary>
/// <param name="TitleInHead">
/// Tên phiếu in ở giữa đầu phiếu, thay cho quốc hiệu (bản giấy TTNB QT-KNGT/BM03 không có quốc hiệu): logo, tên phiếu, mã tài
/// liệu trên cùng một hàng; dòng tiếng Anh của tên phiếu màu xanh, chữ đứng.
/// </param>
/// <param name="UnitInHead">
/// Cùng <see cref="TitleInHead"/>: cột trái in tên cơ quan chủ quản và tên bệnh viện phía trên logo, như bản giấy QT-KNGT/BM10
/// (quyết định 43).
/// </param>
public sealed record TemplateDocumentControl(
    string? Code = null,
    string? IssueNo = null,
    string? EffectiveDate = null,
    bool TitleInHead = false,
    string? CodeLabel = null,
    string? IssueNoLabel = null,
    string? EffectiveDateLabel = null,
    bool UnitInHead = false)
{
    public const int MaxLength = 50;

    public const string DefaultCodeLabel = "Mã số (Code No)";
    public const string DefaultIssueNoLabel = "Lần ban hành (Issue No)";
    public const string DefaultEffectiveDateLabel = "Ngày hiệu lực (Effective Date)";
}

/// <summary>
/// Một mục trên dòng thông tin người bệnh: thông tin trong hồ sơ (<see cref="Patient"/>, xem <see cref="PatientLineSources"/>)
/// hoặc trường của phiếu (<see cref="Field"/>), kèm nhãn (bỏ trống thì dùng nhãn mặc định).
/// <list type="bullet">
/// <item>Giới tính có <see cref="Options"/> (nhãn Nam, nhãn Nữ): in hai ô đánh dấu như bản giấy.</item>
/// <item>Trường chọn của phiếu: in nhãn và các ô lựa chọn trên dòng; <see cref="Other"/> là trường chữ in tiếp sau các ô
/// (ví dụ "☐ Khác (Other): …").</item>
/// <item>Trường chữ có <see cref="Options"/> (ví dụ dân tộc: "Kinh", "Khác, ghi rõ"): in các ô như bản giấy; giá trị trùng một
/// lựa chọn (trừ lựa chọn cuối) thì đánh dấu ô đó, giá trị khác thì đánh dấu ô cuối và in giá trị sau ô.</item>
/// <item><see cref="Indent"/>: dòng phụ của dòng trên (không đánh số, thụt vào), đặt ở mục đầu của dòng.</item>
/// </list>
/// </summary>
public sealed record PatientLineItem(
    string? Patient = null,
    string? Field = null,
    string? Label = null,
    IReadOnlyList<string>? Options = null,
    bool Indent = false,
    string? Other = null)
{
    /// <summary>Số lựa chọn tối đa của trường chữ in thành ô.</summary>
    public const int MaxFieldOptions = 6;
}

/// <summary>Thông tin hồ sơ in được trên dòng thông tin người bệnh, kèm nhãn mặc định.</summary>
public static class PatientLineSources
{
    public const string FullName = "fullName";
    public const string DateOfBirth = "dateOfBirth";
    public const string Gender = "gender";
    public const string PatientCode = "patientCode";
    public const string CitizenId = "citizenId";
    public const string PhoneNumber = "phoneNumber";
    public const string Address = "address";

    public const int MaxLines = 20;
    public const int MaxItemsPerLine = 2;

    public static IReadOnlyDictionary<string, string> DefaultLabels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FullName] = "Họ và tên",
        [DateOfBirth] = "Ngày sinh",
        [Gender] = "Giới tính",
        [PatientCode] = "Mã người bệnh",
        [CitizenId] = "Số CCCD",
        [PhoneNumber] = "Điện thoại",
        [Address] = "Địa chỉ",
    };
}

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

    /// <summary>Trường in một dòng "nhãn: giá trị" trên PDF, nên in chung dòng với trường khác được.</summary>
    public static bool CanShareLine(string type) => type is Text or Date or DateTime or Computed;
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
        typeof(TemplateStatement), typeof(TemplateDocumentControl), typeof(PatientLineItem),
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
