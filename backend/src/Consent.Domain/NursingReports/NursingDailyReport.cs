using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

public enum NursingReportStatus
{
    /// <summary>Khoa đang soạn (hoặc Phòng Điều dưỡng đã mở lại): khoa sửa được.</summary>
    Draft,

    /// <summary>Khoa đã gửi: khóa, chỉ Phòng Điều dưỡng mở lại được.</summary>
    Submitted,
}

/// <summary>
/// Báo cáo công tác điều dưỡng hằng ngày của một khoa (quyết định 31): mỗi khoa một báo cáo mỗi ngày, điều dưỡng của khoa
/// lập và gửi; gửi xong thì khóa, cần sửa thì Phòng Điều dưỡng mở lại (ghi lý do). Phòng Điều dưỡng tổng hợp báo cáo đã gửi
/// của các khoa.
/// <para>
/// <see cref="FiguresJson"/> là số liệu và phần tick (không có thông tin người bệnh). <see cref="Notes"/> là phần báo cáo nhập
/// thêm, thường có họ tên, chẩn đoán người bệnh: được mã hóa khi lưu và che trong nhật ký.
/// </para>
/// </summary>
public sealed class NursingDailyReport : Entity, IAuditableEntity
{
    public const int FiguresMaxLength = 20_000;
    public const int NotesMaxLength = 20_000;
    public const int ReopenReasonMaxLength = 500;

    // Tên tham số trùng tên thuộc tính để EF Core dùng được hàm dựng này khi đọc từ CSDL.
    private NursingDailyReport(Guid id, Guid departmentId, DateOnly reportDate, Guid createdByUserId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        DepartmentId = departmentId;
        ReportDate = reportDate;
        Status = NursingReportStatus.Draft;
        FiguresJson = "{}";
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedByUserId = createdByUserId;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid DepartmentId { get; private set; }

    /// <summary>Ngày báo cáo (ngày của bệnh viện).</summary>
    public DateOnly ReportDate { get; private set; }

    public NursingReportStatus Status { get; private set; }

    /// <summary>Số liệu và phần tick dạng JSON; cấu trúc do danh mục mục báo cáo ở tầng Application quy định.</summary>
    public string FiguresJson { get; private set; }

    /// <summary>Phần báo cáo nhập thêm (các vấn đề bất thường): có thể chứa thông tin người bệnh.</summary>
    [SensitiveData]
    public string? Notes { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    /// <summary>Lần mở lại gần nhất (nếu có): người mở, lúc mở, lý do.</summary>
    public Guid? ReopenedByUserId { get; private set; }

    public DateTimeOffset? ReopenedAtUtc { get; private set; }

    public string? ReopenReason { get; private set; }

    public bool IsSubmitted => Status == NursingReportStatus.Submitted;

    public static NursingDailyReport Create(
        Guid departmentId, DateOnly reportDate, string figuresJson, string? notes, Guid userId, DateTimeOffset nowUtc)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Báo cáo điều dưỡng phải thuộc một khoa.");
        }

        var report = new NursingDailyReport(Guid.CreateVersion7(), departmentId, reportDate, userId, nowUtc);
        report.SetContent(figuresJson, notes);
        return report;
    }

    /// <summary>Sửa nội dung khi báo cáo còn là nháp (hoặc đã được mở lại).</summary>
    public void Update(string figuresJson, string? notes, Guid userId, DateTimeOffset nowUtc)
    {
        EnsureDraft();
        SetContent(figuresJson, notes);
        UpdatedByUserId = userId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Gửi báo cáo: khóa lại, chỉ Phòng Điều dưỡng mở lại được.</summary>
    public void Submit(Guid userId, DateTimeOffset nowUtc)
    {
        EnsureDraft();
        Status = NursingReportStatus.Submitted;
        SubmittedByUserId = userId;
        SubmittedAtUtc = nowUtc;
        UpdatedByUserId = userId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Phòng Điều dưỡng mở lại báo cáo đã gửi để khoa sửa và gửi lại; bắt buộc ghi lý do.</summary>
    public void Reopen(string reason, Guid userId, DateTimeOffset nowUtc)
    {
        if (!IsSubmitted)
        {
            throw new DomainException("Chỉ mở lại được báo cáo đã gửi.");
        }

        var normalized = (reason ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > ReopenReasonMaxLength)
        {
            throw new DomainException($"Lý do mở lại phải có từ 1 đến {ReopenReasonMaxLength} ký tự.");
        }

        Status = NursingReportStatus.Draft;
        ReopenedByUserId = userId;
        ReopenedAtUtc = nowUtc;
        ReopenReason = normalized;
        UpdatedByUserId = userId;
        UpdatedAtUtc = nowUtc;
    }

    private void EnsureDraft()
    {
        if (IsSubmitted)
        {
            throw new DomainException("Báo cáo đã gửi, không sửa được. Cần sửa thì đề nghị Phòng Điều dưỡng mở lại.");
        }
    }

    private void SetContent(string figuresJson, string? notes)
    {
        var figures = (figuresJson ?? string.Empty).Trim();
        if (figures.Length == 0 || figures.Length > FiguresMaxLength)
        {
            throw new DomainException("Số liệu báo cáo không hợp lệ.");
        }

        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text is { Length: > NotesMaxLength })
        {
            throw new DomainException($"Phần báo cáo nhập thêm tối đa {NotesMaxLength} ký tự.");
        }

        FiguresJson = figures;
        Notes = text;
    }
}
