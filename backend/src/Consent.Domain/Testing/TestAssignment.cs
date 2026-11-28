using Consent.Domain.Common;

namespace Consent.Domain.Testing;

/// <summary>Kết quả kiểm thử người được phân công báo cáo (quyết định 49).</summary>
public enum TestResult
{
    /// <summary>Đạt: tính năng chạy đúng như mô tả.</summary>
    Passed,

    /// <summary>Đạt một phần: dùng được nhưng còn lỗi hoặc điểm cần sửa.</summary>
    PartiallyPassed,

    /// <summary>Không đạt: tính năng chưa dùng được.</summary>
    Failed,
}

/// <summary>
/// Phân công kiểm thử một tính năng mới cho một người, trong một khoảng ngày (quyết định 49). Người được phân công kiểm thử
/// (thường trên môi trường UAT, quyết định 50) và phải gửi báo cáo: kết quả, lỗi hoặc vấn đề phát hiện, đề xuất nâng cấp.
/// Người quản lý kiểm thử ghi hướng xử lý để nâng cấp phần mềm; báo cáo đã có hướng xử lý thì người kiểm thử không sửa nữa.
/// Không xóa phân công: hủy thì ghi người hủy, lúc hủy.
/// </summary>
public sealed class TestAssignment : Entity, IAuditableEntity
{
    public const int VersionMaxLength = 100;
    public const int FeatureMaxLength = 200;
    public const int TextMaxLength = 4000;
    public const int ReviewNoteMaxLength = 2000;

    /// <summary>Một phân công dài tối đa bấy nhiêu ngày (tính cả hai ngày).</summary>
    public const int MaxDays = 366;

    private TestAssignment(Guid id)
        : base(id)
    {
    }

    /// <summary>Đợt kiểm thử, thường là bản cài hoặc gói (ví dụ "Gói 71 – 21/11/2026").</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>Tính năng cần kiểm thử.</summary>
    public string Feature { get; private set; } = string.Empty;

    /// <summary>Nội dung, cách kiểm thử (không bắt buộc).</summary>
    public string? Instructions { get; private set; }

    public Guid AssigneeUserId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Hạn gửi báo cáo (tính cả ngày này).</summary>
    public DateOnly DueDate { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public TestResult? Result { get; private set; }

    /// <summary>
    /// Lỗi, vấn đề phát hiện (bắt buộc khi không đạt hoặc đạt một phần). Người kiểm thử tự ghi, có thể nhắc tới người bệnh: lưu
    /// mã hóa, nhật ký hệ thống chỉ ghi <c>***</c>.
    /// </summary>
    [SensitiveData]
    public string? Findings { get; private set; }

    /// <summary>Đề xuất nâng cấp, cải tiến (lưu mã hóa, nhật ký chỉ ghi <c>***</c>).</summary>
    [SensitiveData]
    public string? Suggestions { get; private set; }

    public DateTimeOffset? ReportedAtUtc { get; private set; }

    /// <summary>Ngày gửi báo cáo theo giờ bệnh viện (so với hạn để biết trễ hạn).</summary>
    public DateOnly? ReportedOn { get; private set; }

    /// <summary>Hướng xử lý của người quản lý kiểm thử (ví dụ chuyển nhóm phát triển sửa trong bản sau).</summary>
    public string? ReviewNote { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public bool IsCancelled => CancelledAtUtc is not null;

    public bool IsReported => ReportedAtUtc is not null;

    public bool IsReviewed => ReviewedAtUtc is not null;

    /// <summary>Báo cáo gửi sau hạn.</summary>
    public bool IsLate => ReportedOn is { } reportedOn && reportedOn > DueDate;

    public static TestAssignment Create(
        string version,
        string feature,
        string? instructions,
        Guid assigneeUserId,
        DateOnly startDate,
        DateOnly dueDate,
        Guid createdByUserId,
        DateTimeOffset nowUtc)
    {
        if (assigneeUserId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new DomainException("Thiếu người được phân công hoặc người phân công.");
        }

        var assignment = new TestAssignment(Guid.CreateVersion7())
        {
            AssigneeUserId = assigneeUserId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };
        assignment.SetDetails(version, feature, instructions, startDate, dueDate);
        return assignment;
    }

    /// <summary>Sửa đợt, tính năng, nội dung, thời gian; chỉ khi chưa báo cáo và chưa hủy.</summary>
    public void Update(string version, string feature, string? instructions, DateOnly startDate, DateOnly dueDate)
    {
        EnsureOpen();
        if (IsReported)
        {
            throw new DomainException("Người kiểm thử đã gửi báo cáo, không sửa phân công được nữa.");
        }

        SetDetails(version, feature, instructions, startDate, dueDate);
    }

    /// <summary>Hủy phân công chưa báo cáo.</summary>
    public void Cancel(Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người hủy phân công.");
        }

        EnsureOpen();
        if (IsReported)
        {
            throw new DomainException("Người kiểm thử đã gửi báo cáo, không hủy phân công được.");
        }

        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }

    /// <summary>
    /// Người được phân công gửi (hoặc sửa) báo cáo: từ ngày bắt đầu, chưa có hướng xử lý. Gửi sau hạn vẫn nhận, ghi là trễ hạn.
    /// </summary>
    public void Report(Guid userId, TestResult result, string? findings, string? suggestions, DateOnly today, DateTimeOffset nowUtc)
    {
        if (userId != AssigneeUserId)
        {
            throw new DomainException("Chỉ người được phân công gửi báo cáo kiểm thử.");
        }

        EnsureOpen();
        if (IsReviewed)
        {
            throw new DomainException("Báo cáo đã có hướng xử lý, không sửa được nữa.");
        }

        if (today < StartDate)
        {
            throw new DomainException("Chưa tới ngày kiểm thử.");
        }

        if (!Enum.IsDefined(result))
        {
            throw new DomainException("Chọn kết quả kiểm thử.");
        }

        var foundText = Text(findings, TextMaxLength, "Lỗi, vấn đề phát hiện");
        if (result != TestResult.Passed && foundText is null)
        {
            throw new DomainException("Ghi lỗi hoặc vấn đề phát hiện khi kết quả không đạt hoặc đạt một phần.");
        }

        Result = result;
        Findings = foundText;
        Suggestions = Text(suggestions, TextMaxLength, "Đề xuất");
        ReportedAtUtc = nowUtc;
        ReportedOn = today;
    }

    /// <summary>Người quản lý kiểm thử ghi (hoặc sửa) hướng xử lý cho báo cáo đã gửi.</summary>
    public void Review(Guid userId, string note, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người ghi hướng xử lý.");
        }

        if (!IsReported)
        {
            throw new DomainException("Chưa có báo cáo kiểm thử.");
        }

        ReviewNote = Text(note, ReviewNoteMaxLength, "Hướng xử lý") ?? throw new DomainException("Ghi hướng xử lý.");
        ReviewedByUserId = userId;
        ReviewedAtUtc = nowUtc;
    }

    private void EnsureOpen()
    {
        if (IsCancelled)
        {
            throw new DomainException("Phân công đã hủy.");
        }
    }

    private void SetDetails(string version, string feature, string? instructions, DateOnly startDate, DateOnly dueDate)
    {
        var versionText = Text(version, VersionMaxLength, "Đợt kiểm thử") ?? throw new DomainException("Nhập đợt kiểm thử.");
        var featureText = Text(feature, FeatureMaxLength, "Tính năng") ?? throw new DomainException("Nhập tính năng cần kiểm thử.");
        if (dueDate < startDate)
        {
            throw new DomainException("Hạn báo cáo phải sau hoặc bằng ngày bắt đầu.");
        }

        if (dueDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"Phân công tối đa {MaxDays} ngày.");
        }

        Version = versionText;
        Feature = featureText;
        Instructions = Text(instructions, TextMaxLength, "Nội dung kiểm thử");
        StartDate = startDate;
        DueDate = dueDate;
    }

    private static string? Text(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= maxLength ? text : throw new DomainException($"{name} tối đa {maxLength} ký tự.");
    }
}
