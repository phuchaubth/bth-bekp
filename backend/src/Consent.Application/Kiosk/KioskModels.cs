using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;

namespace Consent.Application.Kiosk;

/// <summary>
/// Cấu hình màn hình kiosk (quyết định 24): khoa của máy kiosk, các mẫu người bệnh tự làm (theo thứ tự) và một ô lời xác
/// nhận chung cho cả bộ (gộp từ các mẫu).
/// </summary>
/// <param name="Ready">Có ít nhất một mẫu dùng được: chưa sẵn sàng thì kiosk mời người bệnh đến quầy.</param>
public sealed record KioskConfigDto(
    string DepartmentName,
    bool Ready,
    IReadOnlyList<KioskTemplateDto> Templates,
    TemplateStatement? Statement);

/// <summary>Một mẫu trên kiosk, kèm định nghĩa để hiện câu hỏi và nội dung phiếu.</summary>
public sealed record KioskTemplateDto(Guid Id, string Code, string Name, TemplateDefinition Definition);

/// <summary>Thông tin đọc từ mã QR trên thẻ CCCD/căn cước (máy kiosk đọc và tách trường trước khi gửi).</summary>
public sealed record KioskCardData(
    string? FullName,
    Gender? Gender,
    string? PermanentAddress,
    DateOnly? IssueDate,
    string? OldIdNumber);

/// <summary>Thông tin người bệnh tự khai trên kiosk (khi không có thẻ hoặc mã y tế).</summary>
public sealed record KioskSelfReport(
    string? FullName,
    Gender? Gender,
    string? PhoneNumber,
    string? CurrentAddress);

/// <summary>
/// Người bệnh tự nhận diện trên kiosk. Luôn có ngày sinh: quét thẻ thì lấy từ thẻ, nhập mã thì người bệnh nhập để đối chiếu,
/// tự khai thì là ngày sinh khai.
/// <list type="bullet">
/// <item><see cref="KioskIdentityMethod.CitizenIdCard"/>: <see cref="CitizenId"/> và <see cref="Card"/> đọc từ mã QR.</item>
/// <item><see cref="KioskIdentityMethod.LookupCode"/>: một trong hai, <see cref="PatientCode"/> (mã y tế) hoặc
/// <see cref="CitizenId"/>.</item>
/// <item><see cref="KioskIdentityMethod.SelfReported"/>: <see cref="SelfReport"/>, số CCCD nếu có.</item>
/// </list>
/// </summary>
public sealed record KioskIdentityRequest(
    KioskIdentityMethod Method,
    DateOnly? DateOfBirth,
    string? CitizenId = null,
    string? PatientCode = null,
    KioskCardData? Card = null,
    KioskSelfReport? SelfReport = null);

/// <summary>
/// Kết quả nhận diện để người bệnh xác nhận "Đây có phải thông tin của ông/bà?". Số CCCD luôn che bớt; số điện thoại
/// không bao giờ hiện. Nhận diện bằng mã y tế/số CCCD thì không hiện địa chỉ trong hồ sơ (chỉ báo đã có), để người biết
/// mã và ngày sinh của người khác không xem được nơi ở của họ.
/// </summary>
/// <param name="FoundRecord">Đã có hồ sơ ở bệnh viện (phần mềm này hoặc hệ thống thông tin bệnh viện).</param>
/// <param name="PermanentAddress">Chỉ có khi quét thẻ (địa chỉ in trên thẻ) hoặc tự khai.</param>
/// <param name="MissingFields">Thông tin hồ sơ còn thiếu, kiosk hỏi thêm (hiện là <c>phoneNumber</c>).</param>
/// <param name="PrefilledSources">Nguồn tự điền đã có giá trị (xem <see cref="PrefillSources"/>): câu hỏi lấy từ các nguồn này không cần hỏi lại.</param>
public sealed record KioskIdentityDto(
    KioskIdentityMethod Method,
    bool FoundRecord,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenIdMasked,
    string? PatientCode,
    string? PermanentAddress,
    bool HasAddressOnFile,
    bool HasPhoneNumber,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> PrefilledSources,
    IReadOnlyList<KioskInheritedTemplateDto>? Inherited = null);

/// <summary>
/// Mẫu có phiếu lần trước kế thừa được (quyết định 27) và các câu đã trả lời ở phiếu đó. Chỉ có tên trường, không có câu trả lời:
/// kiosk hỏi một câu "giữ như lần trước", không hiện lại thông tin cũ.
/// </summary>
public sealed record KioskInheritedTemplateDto(Guid TemplateId, IReadOnlyList<string> Keys);

/// <summary>Câu trả lời cho một mẫu: dữ liệu các trường và Đồng ý/Không đồng ý (nếu mẫu có hỏi).</summary>
public sealed record KioskFormAnswers(Guid TemplateId, JsonObject? Data, ConsentDecision? Decision);

/// <summary>
/// Nộp và ký cả bộ trên kiosk: nhận diện lại (máy chủ không giữ trạng thái giữa các bước), thông tin bổ sung, câu trả lời
/// của từng mẫu, một lời xác nhận và một chữ ký cho cả bộ.
/// </summary>
/// <param name="KeepPrevious">
/// Người bệnh chọn "Không thay đổi" so với lần trước (quyết định 27): câu không trả lời lại được lấy theo phiếu đã hoàn tất gần
/// nhất của cùng mẫu (máy chủ tự tìm lại, không nhận câu trả lời cũ từ kiosk).
/// </param>
public sealed record KioskSubmitRequest(
    KioskIdentityRequest? Identity,
    string? PhoneNumber,
    IReadOnlyList<KioskFormAnswers>? Forms,
    string? Statement,
    string? Image,
    bool KeepPrevious = false);

public sealed record KioskSubmittedFormDto(Guid Id, string FormNo, string TemplateName, ConsentDecision? Decision);

/// <summary>Kết quả để màn hình hoàn tất: số phiếu và mã y tế đưa nhân viên ở quầy.</summary>
public sealed record KioskSubmitResultDto(
    string PatientName,
    string? PatientCode,
    Guid? BundleId,
    IReadOnlyList<KioskSubmittedFormDto> Forms,
    DateTimeOffset SignedAtUtc);

/// <summary>Một mẫu trong cấu hình kiosk (màn hình Cấu hình hệ thống), kèm lý do chưa dùng được trên kiosk (nếu có).</summary>
public sealed record KioskTemplateSettingDto(Guid Id, string Code, string Name, IReadOnlyList<string> Problems);

public sealed record KioskSettingsDto(IReadOnlyList<KioskTemplateSettingDto> Templates, DateTimeOffset? UpdatedAtUtc);

/// <summary>Các mẫu người bệnh tự làm trên kiosk, theo thứ tự làm. Danh sách rỗng: kiosk chưa có phiếu nào.</summary>
public sealed record UpdateKioskSettingsRequest(IReadOnlyList<Guid>? TemplateIds);
