using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;

namespace Consent.Application.ConsentForms;

/// <summary>Thông tin in ở đầu phiếu (mục <c>Hospital</c>). Mặc định là chỗ trống để bệnh viện cấu hình.</summary>
public sealed class HospitalOptions
{
    public const string SectionName = "Hospital";

    /// <summary>Cơ quan chủ quản, ví dụ "SỞ Y TẾ ...".</summary>
    public string ParentAgency { get; set; } = "SỞ Y TẾ [TỈNH/THÀNH PHỐ]";

    public string Name { get; set; } = "BỆNH VIỆN [TÊN BỆNH VIỆN]";

    /// <summary>Địa danh ghi ở dòng ngày tháng, ví dụ "Hà Nội".</summary>
    public string Place { get; set; } = "[Địa danh]";
}

/// <summary>Mọi dữ liệu cần để dựng PDF một phiếu (chỉ gồm trường đang hiển thị).</summary>
public sealed record ConsentPdfDocument(
    HospitalOptions Hospital,
    string DepartmentName,
    string TemplateCode,
    int TemplateVersionNo,
    string FormNo,
    TemplateDefinition Definition,
    JsonObject Data,
    IReadOnlyDictionary<string, ScoreResult> Scores,
    ConsentPatientInfo Patient,
    ConsentRepresentativeInfo? Representative,
    DateOnly DocumentDate,
    bool IsFinal,
    ConsentDecision? Decision = null,
    ConsentPdfSignature? PatientSignature = null,
    ConsentPdfConfirmation? HospitalConfirmation = null,
    IReadOnlyList<ConsentPdfConfirmation?>? StepConfirmations = null);

/// <summary>Chữ ký tay của người bệnh/người đại diện: ảnh PNG đã chuẩn hóa, thời điểm ký (giờ Việt Nam).</summary>
public sealed record ConsentPdfSignature(byte[] ImagePng, DateTimeOffset SignedAt);

/// <summary>
/// Xác nhận bằng tài khoản phía bệnh viện: họ tên, chức danh, thời điểm (giờ Việt Nam). Với các bước trung gian,
/// <see cref="ConsentPdfDocument.StepConfirmations"/> theo đúng thứ tự <c>signatures.steps</c> (bước chưa làm là <c>null</c>).
/// </summary>
public sealed record ConsentPdfConfirmation(string Name, string? Title, DateTimeOffset ConfirmedAt, byte[]? ImagePng = null);

public sealed record ConsentPdfFile(byte[] Content, string FileName);

/// <summary>Dựng PDF khổ A4 của phiếu (phông có đủ dấu tiếng Việt được nhúng vào file).</summary>
public interface IConsentPdfRenderer
{
    byte[] Render(ConsentPdfDocument document);
}

/// <summary>Nhãn tiếng Việt dùng khi in phiếu, xuất dữ liệu và báo cáo (khớp với nhãn trên giao diện).</summary>
public static class VietnameseLabels
{
    public static string Gender(Gender gender) => gender == Domain.Patients.Gender.Male ? "Nam" : "Nữ";

    public static string Status(ConsentFormStatus status) => status switch
    {
        ConsentFormStatus.Draft => "Nháp",
        ConsentFormStatus.PendingSignature => "Chờ ký",
        ConsentFormStatus.Completed => "Hoàn tất",
        ConsentFormStatus.Cancelled => "Đã hủy",
        ConsentFormStatus.Revoked => "Đã thu hồi",
        _ => status.ToString(),
    };

    public static string Decision(ConsentDecision decision) => decision == ConsentDecision.Agreed ? "Đồng ý" : "Từ chối";

    public static string SignerKind(ConsentSignerKind kind) => kind == ConsentSignerKind.Patient ? "Người bệnh" : "Người đại diện";

    public static string Relationship(RepresentativeRelationship relationship) => relationship switch
    {
        RepresentativeRelationship.Father => "Cha",
        RepresentativeRelationship.Mother => "Mẹ",
        RepresentativeRelationship.Spouse => "Vợ/Chồng",
        RepresentativeRelationship.Child => "Con",
        RepresentativeRelationship.Sibling => "Anh/chị/em ruột",
        RepresentativeRelationship.Guardian => "Người giám hộ",
        RepresentativeRelationship.Relative => "Người thân khác",
        _ => "Khác",
    };

    /// <summary>Tuổi tròn tại ngày <paramref name="onDate"/>.</summary>
    public static int Age(DateOnly dateOfBirth, DateOnly onDate)
    {
        var age = onDate.Year - dateOfBirth.Year;
        return onDate < dateOfBirth.AddYears(age) ? age - 1 : age;
    }
}
