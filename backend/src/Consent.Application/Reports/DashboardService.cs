using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;

namespace Consent.Application.Reports;

/// <summary>
/// Trang tổng quan (màn hình 2) trong phạm vi khoa của người dùng: số phiếu nháp, chờ ký, hoàn tất hôm nay, thu hồi
/// 30 ngày qua; phiếu chờ người dùng xác nhận (người bệnh đã ký, bước xác nhận đang chờ thuộc vai trò của người dùng); phiếu người
/// dùng lập chưa hoàn tất; số phiếu lập và hoàn tất từng ngày trong 14 ngày gần nhất.
/// </summary>
public sealed class DashboardService(
    IConsentFormRepository forms,
    IConsentStatisticsRepository statistics,
    IConsentTemplateRepository templates,
    IClock clock,
    ICurrentUser currentUser)
{
    public const int ListSize = 5;
    public const int ChartDays = 14;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var scope = new ConsentFormSearch(
            Status: null, TemplateId: null, PatientId: null, DepartmentId: null, FromUtc: null, ToUtc: null, Keyword: null,
            Page: 1, PageSize: 1, currentUser.HasPermission(Permissions.ScopeAllDepartments), currentUser.DepartmentId, currentUser.UserId);
        var today = ReportDates.Today(clock.UtcNow);
        var todayStart = ConsentFormService.StartOfDayUtc(today)!.Value;
        var tomorrow = todayStart.AddDays(1);
        var origin = todayStart.AddDays(1 - ChartDays);

        var drafts = await forms.CountAsync(scope with { Status = ConsentFormStatus.Draft }, cancellationToken).ConfigureAwait(false);
        var pending = await forms.CountAsync(scope with { Status = ConsentFormStatus.PendingSignature }, cancellationToken).ConfigureAwait(false);
        var completedToday = await statistics.CountClosedAsync(scope, revoked: false, todayStart, tomorrow, cancellationToken).ConfigureAwait(false);
        var revoked = await statistics.CountClosedAsync(scope, revoked: true, todayStart.AddDays(-29), tomorrow, cancellationToken)
            .ConfigureAwait(false);

        var awaiting = currentUser.HasPermission(Permissions.ConsentsSign)
            ? await AwaitingMyConfirmationAsync(scope, cancellationToken).ConfigureAwait(false)
            : new DashboardFormList(0, []);

        var mine = currentUser.UserId is { } userId
            ? await statistics.OpenFormsCreatedByAsync(userId, ListSize, cancellationToken).ConfigureAwait(false)
            : new DashboardFormList(0, []);

        var created = await statistics.CountByCreatedDayAsync(scope with { FromUtc = origin, ToUtc = tomorrow }, origin, cancellationToken)
            .ConfigureAwait(false);
        var completed = await statistics.CountCompletedByDayAsync(scope, origin, tomorrow, cancellationToken).ConfigureAwait(false);
        var days = Enumerable.Range(0, ChartDays)
            .Select(i => new DashboardDay(
                today.AddDays(i + 1 - ChartDays),
                created.Where(c => c.DayIndex == i).Sum(c => c.Count),
                completed.Where(c => c.DayIndex == i).Sum(c => c.Count)))
            .ToList();

        return new DashboardDto(drafts, pending, completedToday, revoked, awaiting, mine, days);
    }

    /// <summary>
    /// Phiếu đang đến lượt người dùng xác nhận: người bệnh đã ký và bước đang chờ thuộc vai trò của người dùng. Bước đang chờ
    /// là bước trung gian đầu tiên chưa xác nhận theo định nghĩa mẫu; hết bước trung gian thì là xác nhận cuối. Bỏ phiếu người
    /// dùng đã xác nhận một bước (mỗi bước cần một người khác), cùng điều kiện hiện nút xác nhận trên trang phiếu.
    /// </summary>
    private async Task<DashboardFormList> AwaitingMyConfirmationAsync(ConsentFormSearch scope, CancellationToken cancellationToken)
    {
        // Chỉ các phiên bản mẫu mà người dùng có vai trò ở một bước nào đó (bước trung gian hoặc xác nhận cuối).
        var versions = new Dictionary<Guid, (IReadOnlyList<TemplateConfirmationStep> Steps, string FinalRole)>();
        foreach (var template in await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
        {
            foreach (var version in template.Versions)
            {
                var steps = ConsentTemplateService.ReadDefinition(version).Signatures?.Steps ?? [];
                if (currentUser.IsInRole(template.HospitalSignerRoleCode) || steps.Any(s => currentUser.IsInRole(s.RoleCode)))
                {
                    versions[version.Id] = (steps, template.HospitalSignerRoleCode);
                }
            }
        }

        if (versions.Count == 0)
        {
            return new DashboardFormList(0, []);
        }

        var candidates = await statistics.ListAwaitingConfirmationAsync(scope, versions.Keys, cancellationToken).ConfigureAwait(false);
        var myTurn = candidates
            .Where(c => versions.TryGetValue(c.TemplateVersionId, out var template) && IsMyTurn(c, template.Steps, template.FinalRole))
            .OrderBy(c => c.PatientSignedAtUtc)
            .ToList();
        var shown = myTurn.Take(ListSize).Select(c => c.Id).ToList();
        var items = shown.Count == 0
            ? []
            : await statistics.GetSignedFormItemsAsync(shown, cancellationToken).ConfigureAwait(false);
        return new DashboardFormList(
            myTurn.Count, [.. shown.Select(id => items.FirstOrDefault(i => i.Id == id)).OfType<DashboardFormItem>()]);
    }

    private bool IsMyTurn(AwaitingConfirmationCandidate form, IReadOnlyList<TemplateConfirmationStep> steps, string finalRole)
    {
        if (form.ConfirmedSteps.Any(s => s.UserId is { } userId && userId == currentUser.UserId))
        {
            return false;
        }

        var pending = steps.FirstOrDefault(step => !form.ConfirmedSteps.Any(s => s.StepKey == step.Key));
        return currentUser.IsInRole(pending?.RoleCode ?? finalRole);
    }
}
