using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Domain.Common;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;

namespace Consent.Application.Exports;

/// <summary>
/// Một phiếu khi xuất dữ liệu, đọc từ CSDL (cột mã hóa đã được giải mã). Thông tin người bệnh/người đại diện "hiện tại"
/// chỉ dùng khi phiếu chưa trình ký; phiếu đã trình ký dùng bản đóng băng <see cref="SnapshotJson"/>.
/// </summary>
public sealed record ConsentFormExportRecord
{
    public required Guid Id { get; init; }

    public required string FormNo { get; init; }

    public required ConsentFormStatus Status { get; init; }

    public required string TemplateCode { get; init; }

    public required string TemplateName { get; init; }

    public required Guid TemplateVersionId { get; init; }

    public required int TemplateVersionNo { get; init; }

    public required string DepartmentName { get; init; }

    public required string CreatedByName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? SubmittedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public DateTimeOffset? CancelledAtUtc { get; init; }

    public string? CancelReason { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public string? RevokeReason { get; init; }

    public ConsentDecision? Decision { get; init; }

    public required ConsentSignerKind SignerKind { get; init; }

    public required Guid PatientId { get; init; }

    public required string PatientName { get; init; }

    public string? PatientCode { get; init; }

    public required DateOnly PatientDateOfBirth { get; init; }

    public required Gender PatientGender { get; init; }

    public string? PatientCitizenId { get; init; }

    public string? PatientPhoneNumber { get; init; }

    public string? PatientAddress { get; init; }

    public Guid? RepresentativeId { get; init; }

    public string? RepresentativeName { get; init; }

    public RepresentativeRelationship? RepresentativeRelationship { get; init; }

    public string? RepresentativeCitizenId { get; init; }

    public string? RepresentativePhoneNumber { get; init; }

    /// <summary>Thông tin người bệnh và người ký đóng băng khi trình ký (JSON, đã giải mã).</summary>
    public string? SnapshotJson { get; init; }

    public DateTimeOffset? PatientSignedAtUtc { get; init; }

    public string? HospitalSignerName { get; init; }

    public string? HospitalSignerTitle { get; init; }

    public string? ReplacesFormNo { get; init; }

    public string? SignedPdfSha256 { get; init; }

    /// <summary>Nội dung phiếu (JSON, đã giải mã); chỉ đọc khi xuất kèm nội dung theo mẫu.</summary>
    public string? DataJson { get; init; }
}

/// <summary>Dữ liệu của một dòng khi ghi ra file: bản ghi, người bệnh/người đại diện đã xác định, có che dữ liệu hay không.</summary>
internal sealed record ExportRowContext(
    ConsentFormExportRecord Record,
    ConsentPatientInfo Patient,
    ConsentRepresentativeInfo? Representative,
    bool IncludeSensitiveData);

internal sealed record ConsentFormExportColumn(
    string Key,
    string Header,
    string Group,
    TabularCellKind Kind,
    double Width,
    bool Default,
    bool Sensitive,
    Func<ExportRowContext, object?> Value)
{
    public TabularColumn ToTabular() => new(Header, Kind, Width);
}

/// <summary>Danh mục cột khi xuất phiếu (tiêu đề tiếng Việt). Cột nhạy cảm bị che, trừ khi được phép và chọn xuất đầy đủ.</summary>
public static class ConsentFormExportColumns
{
    private const string FormGroup = "Phiếu";
    private const string PatientGroup = "Người bệnh";
    private const string SignerGroup = "Người ký";
    private const string HospitalGroup = "Phía bệnh viện";
    private const string ClosureGroup = "Hủy và thu hồi";

    internal static IReadOnlyList<ConsentFormExportColumn> All { get; } =
    [
        new("formNo", "Số phiếu", FormGroup, TabularCellKind.Text, 17, true, false, r => r.Record.FormNo),
        new("status", "Trạng thái", FormGroup, TabularCellKind.Text, 12, true, false, r => VietnameseLabels.Status(r.Record.Status)),
        new("templateCode", "Mã mẫu", FormGroup, TabularCellKind.Text, 9, true, false, r => r.Record.TemplateCode),
        new("templateName", "Tên mẫu phiếu", FormGroup, TabularCellKind.Text, 40, true, false, r => r.Record.TemplateName),
        new("templateVersion", "Phiên bản mẫu", FormGroup, TabularCellKind.Number, 9, false, false, r => r.Record.TemplateVersionNo),
        new("department", "Khoa/phòng", FormGroup, TabularCellKind.Text, 24, true, false, r => r.Record.DepartmentName),
        new("createdBy", "Người lập", FormGroup, TabularCellKind.Text, 22, true, false, r => r.Record.CreatedByName),
        new("createdAt", "Thời điểm lập", FormGroup, TabularCellKind.DateTime, 17, true, false, r => Local(r.Record.CreatedAtUtc)),
        new("submittedAt", "Thời điểm trình ký", FormGroup, TabularCellKind.DateTime, 17, false, false, r => Local(r.Record.SubmittedAtUtc)),
        new("completedAt", "Thời điểm hoàn tất", FormGroup, TabularCellKind.DateTime, 17, true, false, r => Local(r.Record.CompletedAtUtc)),
        new("replacesFormNo", "Thay thế cho phiếu", FormGroup, TabularCellKind.Text, 17, false, false, r => r.Record.ReplacesFormNo),
        new("signedPdfSha256", "SHA-256 của PDF đã ký", FormGroup, TabularCellKind.Text, 66, false, false, r => r.Record.SignedPdfSha256),
        new("patientCode", "Mã người bệnh", PatientGroup, TabularCellKind.Text, 14, true, false, r => r.Patient.PatientCode),
        new("patientName", "Họ tên người bệnh", PatientGroup, TabularCellKind.Text, 26, true, false, r => r.Patient.FullName),
        new("dateOfBirth", "Ngày sinh", PatientGroup, TabularCellKind.Date, 11, true, false, r => r.Patient.DateOfBirth),
        new("gender", "Giới tính", PatientGroup, TabularCellKind.Text, 9, true, false, r => VietnameseLabels.Gender(r.Patient.Gender)),
        new("citizenId", "Số CCCD", PatientGroup, TabularCellKind.Text, 15, false, true,
            r => r.IncludeSensitiveData ? r.Patient.CitizenId : SensitiveMask.CitizenId(r.Patient.CitizenId)),
        new("phoneNumber", "Số điện thoại", PatientGroup, TabularCellKind.Text, 13, false, true,
            r => r.IncludeSensitiveData ? r.Patient.PhoneNumber : SensitiveMask.PhoneNumber(r.Patient.PhoneNumber)),
        new("address", "Địa chỉ", PatientGroup, TabularCellKind.Text, 40, false, false, r => r.Patient.Address),
        new("signer", "Người ký", SignerGroup, TabularCellKind.Text, 15, true, false, r => VietnameseLabels.SignerKind(r.Record.SignerKind)),
        new("representativeName", "Họ tên người đại diện", SignerGroup, TabularCellKind.Text, 26, false, false, r => r.Representative?.FullName),
        new("representativeRelationship", "Quan hệ với người bệnh", SignerGroup, TabularCellKind.Text, 16, false, false,
            r => r.Representative is { } representative ? VietnameseLabels.Relationship(representative.Relationship) : null),
        new("representativeCitizenId", "Số CCCD người đại diện", SignerGroup, TabularCellKind.Text, 15, false, true,
            r => r.IncludeSensitiveData ? r.Representative?.CitizenId : SensitiveMask.CitizenId(r.Representative?.CitizenId)),
        new("decision", "Ý kiến", SignerGroup, TabularCellKind.Text, 10, true, false,
            r => r.Record.Decision is { } decision ? VietnameseLabels.Decision(decision) : null),
        new("patientSignedAt", "Thời điểm ký", SignerGroup, TabularCellKind.DateTime, 17, false, false, r => Local(r.Record.PatientSignedAtUtc)),
        new("confirmedBy", "Người xác nhận", HospitalGroup, TabularCellKind.Text, 22, true, false, r => r.Record.HospitalSignerName),
        new("confirmedByTitle", "Chức danh người xác nhận", HospitalGroup, TabularCellKind.Text, 18, false, false, r => r.Record.HospitalSignerTitle),
        new("cancelledAt", "Thời điểm hủy", ClosureGroup, TabularCellKind.DateTime, 17, false, false, r => Local(r.Record.CancelledAtUtc)),
        new("cancelReason", "Lý do hủy", ClosureGroup, TabularCellKind.Text, 30, false, false, r => r.Record.CancelReason),
        new("revokedAt", "Thời điểm thu hồi", ClosureGroup, TabularCellKind.DateTime, 17, false, false, r => Local(r.Record.RevokedAtUtc)),
        new("revokeReason", "Lý do thu hồi", ClosureGroup, TabularCellKind.Text, 30, false, false, r => r.Record.RevokeReason),
    ];

    private static readonly Dictionary<string, ConsentFormExportColumn> ByKey =
        All.ToDictionary(c => c.Key, StringComparer.Ordinal);

    public static bool IsDefined(string key) => key is not null && ByKey.ContainsKey(key);

    /// <summary>Danh mục cột cho giao diện (màn hình 13).</summary>
    public static IReadOnlyList<ExportColumnDto> Describe() =>
        [.. All.Select(c => new ExportColumnDto(c.Key, c.Header, c.Group, c.Default, c.Sensitive))];

    /// <summary>Các cột được chọn, theo thứ tự của danh mục (bỏ trùng).</summary>
    internal static IReadOnlyList<ConsentFormExportColumn> Select(IEnumerable<string> keys)
    {
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        return [.. All.Where(c => selected.Contains(c.Key))];
    }

    /// <summary>Giờ Việt Nam, dạng <see cref="DateTime"/> không kèm múi giờ (để ghi vào ô ngày giờ của Excel).</summary>
    internal static DateTime? Local(DateTimeOffset? utc) => utc?.ToOffset(ConsentFormService.VietnamOffset).DateTime;
}
