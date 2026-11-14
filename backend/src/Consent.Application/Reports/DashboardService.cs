using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;

namespace Consent.Application.Reports;

/// <summary>
/// Trang tổng quan (màn hình 2) trong phạm vi khoa của người dùng: số phiếu nháp, chờ ký, hoàn tất hôm nay, thu hồi
/// 30 ngày qua; phiếu chờ người dùng xác nhận (người bệnh đã ký, người dùng có vai trò xác nhận của mẫu); phiếu người
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

        var awaiting = new DashboardFormList(0, []);
        if (currentUser.HasPermission(Permissions.ConsentsSign))
        {
            var templateIds = (await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
                .Where(t => currentUser.IsInRole(t.HospitalSignerRoleCode))
                .Select(t => t.Id)
                .ToList();
            if (templateIds.Count > 0)
            {
                awaiting = await statistics.AwaitingConfirmationAsync(scope, templateIds, ListSize, cancellationToken).ConfigureAwait(false);
            }
        }

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
}
