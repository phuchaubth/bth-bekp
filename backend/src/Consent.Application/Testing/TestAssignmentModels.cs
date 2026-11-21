using Consent.Domain.Testing;

namespace Consent.Application.Testing;

/// <summary>Trạng thái phân công kiểm thử so với hôm nay (giờ Việt Nam).</summary>
public enum TestAssignmentStatus
{
    /// <summary>Chưa tới ngày bắt đầu.</summary>
    Upcoming,

    /// <summary>Đang trong thời gian kiểm thử, chưa báo cáo.</summary>
    InProgress,

    /// <summary>Quá hạn, chưa báo cáo.</summary>
    Overdue,

    /// <summary>Đã gửi báo cáo (đúng hạn hoặc trễ hạn).</summary>
    Reported,

    /// <summary>Đã hủy.</summary>
    Cancelled,
}

/// <summary>
/// Một phân công kiểm thử (quyết định 49). <see cref="Late"/>: báo cáo gửi sau hạn. <see cref="CanReport"/>: người xem là người
/// được phân công và gửi (sửa) được báo cáo. <see cref="CanEdit"/>: người quản lý kiểm thử còn sửa, hủy được (chưa báo cáo).
/// </summary>
public sealed record TestAssignmentDto(
    Guid Id,
    string Version,
    string Feature,
    string? Instructions,
    Guid AssigneeUserId,
    string AssigneeName,
    string? AssigneeDepartment,
    DateOnly StartDate,
    DateOnly DueDate,
    TestAssignmentStatus Status,
    TestResult? Result,
    string? Findings,
    string? Suggestions,
    DateTimeOffset? ReportedAtUtc,
    bool Late,
    string? ReviewNote,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? CancelledBy,
    DateTimeOffset? CancelledAtUtc,
    bool CanReport,
    bool CanEdit);

/// <summary>Danh sách phân công của một đợt (hoặc mọi đợt) và các đợt kiểm thử đã có.</summary>
public sealed record TestAssignmentListDto(IReadOnlyList<TestAssignmentDto> Items, IReadOnlyList<string> Versions);

/// <summary>Một người được phân công và thời gian của người đó (từ ngày, hạn báo cáo; tính cả hai ngày).</summary>
public sealed record TestAssigneeRequest(Guid UserId, DateOnly StartDate, DateOnly DueDate);

/// <summary>Phân công một tính năng cho nhiều người, mỗi người một thời gian.</summary>
public sealed record CreateTestAssignmentsRequest(
    string? Version,
    string? Feature,
    string? Instructions,
    IReadOnlyList<TestAssigneeRequest>? Assignees);

public sealed record UpdateTestAssignmentRequest(
    string? Version,
    string? Feature,
    string? Instructions,
    DateOnly StartDate,
    DateOnly DueDate);

/// <summary>Báo cáo kiểm thử: kết quả (bắt buộc), lỗi hoặc vấn đề phát hiện (bắt buộc khi chưa đạt), đề xuất.</summary>
public sealed record SubmitTestReportRequest(TestResult? Result, string? Findings, string? Suggestions);

public sealed record ReviewTestReportRequest(string? Note);

/// <summary>Người có thể được phân công kiểm thử: tài khoản đang dùng có vai trò (trừ tài khoản kiosk).</summary>
public sealed record TestCandidateDto(Guid UserId, string FullName, string? Title, string? DepartmentName);

/// <summary>File xuất báo cáo kiểm thử.</summary>
public sealed record TestReportFile(byte[] Content, string ContentType, string FileName);
