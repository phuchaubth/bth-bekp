using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Consent.Domain.Testing;
using Consent.Shared;

namespace Consent.Application.Testing;

/// <summary>
/// Phân công kiểm thử tính năng mới (quyết định 49). Người quản lý kiểm thử (<see cref="Permissions.TestingManage"/>) phân công
/// một tính năng cho từng người, mỗi người một khoảng thời gian; người được phân công kiểm thử (thường trên môi trường UAT,
/// quyết định 50) và gửi báo cáo: kết quả, lỗi hoặc vấn đề phát hiện, đề xuất. Người quản lý xem báo cáo, ghi hướng xử lý để nâng
/// cấp phần mềm và xuất Excel gửi nhóm phát triển. Người được phân công chỉ thấy, báo cáo việc của mình.
/// </summary>
public sealed class TestAssignmentService(
    ITestAssignmentRepository assignments,
    IUserRepository users,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    IEnumerable<ITabularFileWriter> writers)
{
    /// <summary>Một lần phân công tối đa bấy nhiêu người.</summary>
    public const int MaxAssignees = 50;

    private static readonly Error Forbidden = Error.Forbidden("testing.forbidden", "Bạn không có quyền phân công kiểm thử.");

    private static readonly Error NotFound = Error.NotFound("testing.not_found", "Không tìm thấy phân công kiểm thử.");

    private bool CanManage => currentUser.HasPermission(Permissions.TestingManage);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>Phân công của một đợt (bỏ trống là mọi đợt), mới tạo trước, kèm các đợt đã có.</summary>
    public async Task<Result<TestAssignmentListDto>> ListAsync(string? version, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbidden;
        }

        var filter = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        var items = await assignments.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        var versions = await assignments.ListVersionsAsync(cancellationToken).ConfigureAwait(false);
        return new TestAssignmentListDto(await ToDtosAsync(items, cancellationToken).ConfigureAwait(false), versions);
    }

    /// <summary>Việc kiểm thử của người dùng: chưa báo cáo trước (hạn gần trước), rồi đã báo cáo, đã hủy.</summary>
    public async Task<Result<IReadOnlyList<TestAssignmentDto>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.required", "Vui lòng đăng nhập.");
        }

        var items = (await assignments.ListForAssigneeAsync(userId, cancellationToken).ConfigureAwait(false))
            .OrderBy(a => a.IsCancelled)
            .ThenBy(a => a.IsReported)
            .ThenBy(a => a.DueDate)
            .ThenBy(a => a.Feature, StringComparer.CurrentCulture)
            .ToList();
        return Result.Success(await ToDtosAsync(items, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Người có thể được phân công: tài khoản đang dùng có vai trò, trừ tài khoản kiosk; theo tên.</summary>
    public async Task<Result<IReadOnlyList<TestCandidateDto>>> CandidatesAsync(CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbidden;
        }

        var names = await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TestCandidateDto> candidates = [.. (await CandidateUsersAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(u => u.FullName, VietnameseComparer.Instance)
            .Select(u => new TestCandidateDto(u.Id, u.FullName, u.Title, u.DepartmentId is { } d ? names.GetValueOrDefault(d) : null))];
        return Result.Success(candidates);
    }

    /// <summary>Phân công một tính năng cho nhiều người, mỗi người một thời gian.</summary>
    public async Task<Result<IReadOnlyList<TestAssignmentDto>>> CreateAsync(
        CreateTestAssignmentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateDetails(request.Version, request.Feature, request.Instructions, errors);
        var assignees = request.Assignees ?? [];
        if (assignees.Count == 0)
        {
            errors["assignees"] = ["Chọn ít nhất một người kiểm thử."];
        }
        else if (assignees.Count > MaxAssignees)
        {
            errors["assignees"] = [$"Mỗi lần phân công tối đa {MaxAssignees} người."];
        }
        else
        {
            var candidates = (await CandidateUsersAsync(cancellationToken).ConfigureAwait(false)).Select(u => u.Id).ToHashSet();
            var seen = new HashSet<Guid>();
            for (var i = 0; i < assignees.Count; i++)
            {
                var item = assignees[i];
                if (!candidates.Contains(item.UserId))
                {
                    errors[$"assignees[{i}].userId"] = ["Người kiểm thử phải là tài khoản đang dùng."];
                }
                else if (!seen.Add(item.UserId))
                {
                    errors[$"assignees[{i}].userId"] = ["Mỗi người chỉ phân công một lần cho tính năng này."];
                }

                if (DateError(item.StartDate, item.DueDate) is { } dateError)
                {
                    errors[$"assignees[{i}].dueDate"] = [dateError];
                }
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        var now = clock.UtcNow;
        var created = assignees
            .Select(a => TestAssignment.Create(request.Version!, request.Feature!, request.Instructions, a.UserId, a.StartDate, a.DueDate, userId, now))
            .ToList();
        foreach (var assignment in created)
        {
            assignments.Add(assignment);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(await ToDtosAsync(created, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sửa đợt, tính năng, nội dung, thời gian của phân công chưa báo cáo.</summary>
    public async Task<Result<TestAssignmentDto>> UpdateAsync(Guid id, UpdateTestAssignmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage)
        {
            return Forbidden;
        }

        if (await assignments.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } assignment)
        {
            return NotFound;
        }

        if (Locked(assignment) is { } locked)
        {
            return locked;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateDetails(request.Version, request.Feature, request.Instructions, errors);
        if (DateError(request.StartDate, request.DueDate) is { } dateError)
        {
            errors["dueDate"] = [dateError];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        assignment.Update(request.Version!, request.Feature!, request.Instructions, request.StartDate, request.DueDate);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Hủy phân công chưa báo cáo.</summary>
    public async Task<Result<TestAssignmentDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManage || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        if (await assignments.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } assignment)
        {
            return NotFound;
        }

        if (Locked(assignment) is { } locked)
        {
            return locked;
        }

        assignment.Cancel(userId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>
    /// Người được phân công gửi (hoặc sửa) báo cáo từ ngày bắt đầu, khi chưa có hướng xử lý; sau hạn vẫn gửi được, ghi trễ hạn.
    /// Người khác không thấy phân công này (404).
    /// </summary>
    public async Task<Result<TestAssignmentDto>> ReportAsync(Guid id, SubmitTestReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is not { } userId)
        {
            return NotFound;
        }

        if (await assignments.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } assignment
            || assignment.AssigneeUserId != userId)
        {
            return NotFound;
        }

        if (assignment.IsCancelled)
        {
            return Error.BusinessRule("testing.cancelled", "Phân công đã hủy.");
        }

        if (assignment.IsReviewed)
        {
            return Error.BusinessRule("testing.reviewed", "Báo cáo đã có hướng xử lý, không sửa được nữa.");
        }

        var today = Today;
        if (today < assignment.StartDate)
        {
            return Error.BusinessRule(
                "testing.not_started",
                $"Chưa tới ngày kiểm thử (từ {assignment.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}).");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Result is not { } result || !Enum.IsDefined(result))
        {
            errors["result"] = ["Chọn kết quả kiểm thử."];
        }
        else if (result != TestResult.Passed && string.IsNullOrWhiteSpace(request.Findings))
        {
            errors["findings"] = ["Ghi lỗi hoặc vấn đề phát hiện khi kết quả không đạt hoặc đạt một phần."];
        }

        LengthError("findings", request.Findings, TestAssignment.TextMaxLength, "Lỗi, vấn đề phát hiện", errors);
        LengthError("suggestions", request.Suggestions, TestAssignment.TextMaxLength, "Đề xuất", errors);
        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        assignment.Report(userId, request.Result!.Value, request.Findings, request.Suggestions, today, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Người quản lý kiểm thử ghi (hoặc sửa) hướng xử lý cho báo cáo đã gửi; người kiểm thử không sửa báo cáo được nữa.</summary>
    public async Task<Result<TestAssignmentDto>> ReviewAsync(Guid id, ReviewTestReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        if (await assignments.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } assignment)
        {
            return NotFound;
        }

        if (!assignment.IsReported)
        {
            return Error.BusinessRule("testing.not_reported", "Chưa có báo cáo kiểm thử.");
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Error.ValidationField("note", "Ghi hướng xử lý.");
        }

        if (request.Note.Trim().Length > TestAssignment.ReviewNoteMaxLength)
        {
            return Error.ValidationField("note", $"Hướng xử lý tối đa {TestAssignment.ReviewNoteMaxLength} ký tự.");
        }

        assignment.Review(userId, request.Note, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([assignment], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Excel các phân công và báo cáo của một đợt (bỏ trống là mọi đợt), để gửi nhóm phát triển nâng cấp phần mềm.</summary>
    public async Task<Result<TestReportFile>> ExportAsync(string? version, CancellationToken cancellationToken)
    {
        var list = await ListAsync(version, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return list.Error!;
        }

        var writer = writers.First(w => w.Format == ExportFormat.Xlsx);
        var filter = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        var exportedAt = clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset);
        var data = new TabularData(
            "Báo cáo kiểm thử",
            Columns,
            Rows(list.Value.Items),
            [
                new("Đợt kiểm thử", filter ?? "Mọi đợt"),
                new("Số phân công", list.Value.Items.Count.ToString(CultureInfo.InvariantCulture)),
                new("Người xuất", currentUser.UserName ?? string.Empty),
                new("Thời điểm xuất", exportedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
            ]);
        using var stream = new MemoryStream();
        await writer.WriteAsync(data, stream, cancellationToken).ConfigureAwait(false);
        return new TestReportFile(stream.ToArray(), writer.ContentType, $"bao-cao-kiem-thu-{exportedAt:yyyyMMdd-HHmm}.{writer.FileExtension}");
    }

    private static readonly IReadOnlyList<TabularColumn> Columns =
    [
        new("Đợt kiểm thử", Width: 22),
        new("Tính năng", Width: 30),
        new("Nội dung kiểm thử", Width: 40),
        new("Người kiểm thử", Width: 24),
        new("Khoa/phòng", Width: 22),
        new("Từ ngày", TabularCellKind.Date, 12),
        new("Hạn báo cáo", TabularCellKind.Date, 12),
        new("Trạng thái", Width: 16),
        new("Kết quả", Width: 16),
        new("Lỗi, vấn đề phát hiện", Width: 50),
        new("Đề xuất", Width: 40),
        new("Gửi báo cáo lúc", TabularCellKind.DateTime, 18),
        new("Trễ hạn", Width: 10),
        new("Hướng xử lý", Width: 40),
        new("Người ghi hướng xử lý", Width: 24),
    ];

    /// <summary>Chữ của trạng thái và kết quả (dùng cho Excel; giao diện có nhãn riêng).</summary>
    public static string StatusText(TestAssignmentStatus status) => status switch
    {
        TestAssignmentStatus.Upcoming => "Chưa tới ngày",
        TestAssignmentStatus.InProgress => "Đang kiểm thử",
        TestAssignmentStatus.Overdue => "Quá hạn",
        TestAssignmentStatus.Reported => "Đã báo cáo",
        _ => "Đã hủy",
    };

    public static string ResultText(TestResult result) => result switch
    {
        TestResult.Passed => "Đạt",
        TestResult.PartiallyPassed => "Đạt một phần",
        _ => "Không đạt",
    };

    private static async IAsyncEnumerable<IReadOnlyList<object?>> Rows(IReadOnlyList<TestAssignmentDto> items)
    {
        foreach (var a in items)
        {
            yield return
            [
                a.Version,
                a.Feature,
                a.Instructions,
                a.AssigneeName,
                a.AssigneeDepartment,
                a.StartDate,
                a.DueDate,
                StatusText(a.Status),
                a.Result is { } result ? ResultText(result) : null,
                a.Findings,
                a.Suggestions,
                a.ReportedAtUtc?.ToOffset(ConsentFormService.VietnamOffset).DateTime,
                a.Late ? "Trễ hạn" : null,
                a.ReviewNote,
                a.ReviewedBy,
            ];
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static Error? Locked(TestAssignment assignment) =>
        assignment.IsCancelled ? Error.BusinessRule("testing.cancelled", "Phân công đã hủy.")
        : assignment.IsReported ? Error.BusinessRule("testing.reported", "Người kiểm thử đã gửi báo cáo, không sửa hoặc hủy phân công được nữa.")
        : null;

    private static void ValidateDetails(string? version, string? feature, string? instructions, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            errors["version"] = ["Nhập đợt kiểm thử (ví dụ bản cài hoặc gói)."];
        }

        if (string.IsNullOrWhiteSpace(feature))
        {
            errors["feature"] = ["Nhập tính năng cần kiểm thử."];
        }

        LengthError("version", version, TestAssignment.VersionMaxLength, "Đợt kiểm thử", errors);
        LengthError("feature", feature, TestAssignment.FeatureMaxLength, "Tính năng", errors);
        LengthError("instructions", instructions, TestAssignment.TextMaxLength, "Nội dung kiểm thử", errors);
    }

    private static void LengthError(string field, string? value, int maxLength, string name, Dictionary<string, string[]> errors)
    {
        if (value is not null && value.Trim().Length > maxLength && !errors.ContainsKey(field))
        {
            errors[field] = [$"{name} tối đa {maxLength} ký tự."];
        }
    }

    private string? DateError(DateOnly startDate, DateOnly dueDate)
    {
        if (dueDate < startDate)
        {
            return "Hạn báo cáo phải sau hoặc bằng ngày bắt đầu.";
        }

        if (dueDate < Today)
        {
            return "Hạn báo cáo từ hôm nay trở đi.";
        }

        return dueDate.DayNumber - startDate.DayNumber + 1 > TestAssignment.MaxDays
            ? $"Phân công tối đa {TestAssignment.MaxDays} ngày."
            : null;
    }

    /// <summary>Tài khoản đang dùng có ít nhất một vai trò, trừ tài khoản kiosk (chỉ dùng màn hình kiosk).</summary>
    private async Task<IReadOnlyList<User>> CandidateUsersAsync(CancellationToken cancellationToken)
    {
        var allRoles = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        var kiosk = allRoles.Where(r => r.Code == SystemRoles.Kiosk).Select(r => r.Id).ToHashSet();
        return [.. (await users.ListActiveInRolesAsync([.. allRoles.Select(r => r.Id)], cancellationToken).ConfigureAwait(false))
            .Where(u => !u.RoleIds.Any(kiosk.Contains))];
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(CancellationToken cancellationToken) =>
        (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Name);

    private async Task<IReadOnlyList<TestAssignmentDto>> ToDtosAsync(IReadOnlyList<TestAssignment> items, CancellationToken cancellationToken)
    {
        var departmentNames = await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false);
        var people = new Dictionary<Guid, User?>();
        foreach (var id in items.SelectMany(a => new[] { a.AssigneeUserId, a.CreatedByUserId, a.ReviewedByUserId ?? Guid.Empty, a.CancelledByUserId ?? Guid.Empty })
                     .Where(id => id != Guid.Empty)
                     .Distinct())
        {
            people[id] = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }

        string Name(Guid id) => people.GetValueOrDefault(id)?.FullName ?? "Không rõ";
        var today = Today;
        var userId = currentUser.UserId;
        var canManage = CanManage;
        return [.. items.Select(a =>
        {
            var status = a.IsCancelled ? TestAssignmentStatus.Cancelled
                : a.IsReported ? TestAssignmentStatus.Reported
                : today < a.StartDate ? TestAssignmentStatus.Upcoming
                : today > a.DueDate ? TestAssignmentStatus.Overdue
                : TestAssignmentStatus.InProgress;
            var assignee = people.GetValueOrDefault(a.AssigneeUserId);
            return new TestAssignmentDto(
                a.Id,
                a.Version,
                a.Feature,
                a.Instructions,
                a.AssigneeUserId,
                Name(a.AssigneeUserId),
                assignee?.DepartmentId is { } departmentId ? departmentNames.GetValueOrDefault(departmentId) : null,
                a.StartDate,
                a.DueDate,
                status,
                a.Result,
                a.Findings,
                a.Suggestions,
                a.ReportedAtUtc,
                a.IsLate,
                a.ReviewNote,
                a.ReviewedByUserId is { } reviewer ? Name(reviewer) : null,
                a.ReviewedAtUtc,
                Name(a.CreatedByUserId),
                a.CreatedAtUtc,
                a.CancelledByUserId is { } canceller ? Name(canceller) : null,
                a.CancelledAtUtc,
                CanReport: a.AssigneeUserId == userId && !a.IsCancelled && !a.IsReviewed && today >= a.StartDate,
                CanEdit: canManage && !a.IsCancelled && !a.IsReported);
        })];
    }
}
