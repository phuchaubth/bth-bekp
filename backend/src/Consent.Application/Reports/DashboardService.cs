using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;

namespace Consent.Application.Reports;

/// <summary>
/// Trang tổng quan (màn hình 2) trong phạm vi khoa của người dùng: số phiếu nháp, chờ ký, hoàn tất hôm nay, thu hồi
/// 30 ngày qua; phiếu đang đến lượt người dùng xác nhận (người bệnh đã ký; bước trung gian kế tiếp hoặc xác nhận cuối
/// thuộc vai trò của người dùng); phiếu người dùng lập chưa hoàn tất; số phiếu lập và hoàn tất từng ngày trong 14 ngày
/// gần nhất.
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
    /// Phiếu đang đến lượt người dùng xác nhận, cùng quy tắc với nút xác nhận trên phiếu (<see cref="ConfirmationTurn"/>):
    /// bước trung gian kế tiếp thuộc vai trò của người dùng, hoặc đã xong mọi bước và người dùng có vai trò xác nhận cuối;
    /// người đã xác nhận một bước của phiếu thì không còn lượt. Các bước nằm trong định nghĩa của phiên bản mẫu mà phiếu lập
    /// theo, nên CSDL chỉ lọc phiếu chờ xác nhận (trong phạm vi khoa) thuộc các phiên bản mà người dùng có vai trò ở một bước
    /// hoặc ở xác nhận cuối; lượt được xét ở đây. Chỉ đọc thông tin hiển thị của các phiếu được hiện.
    /// </summary>
    private async Task<DashboardFormList> AwaitingMyConfirmationAsync(ConsentFormSearch scope, CancellationToken cancellationToken)
    {
        var flows = new Dictionary<Guid, ConfirmationFlow>();
        foreach (var template in await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
        {
            // Phiếu chỉ lập theo phiên bản đã phát hành (đang dùng hoặc đã ngừng dùng), không theo bản nháp.
            foreach (var version in template.Versions.Where(v => v.Status != TemplateVersionStatus.Draft))
            {
                var steps = ConsentTemplateService.ReadDefinition(version).Signatures?.Steps ?? [];
                if (currentUser.IsInRole(template.HospitalSignerRoleCode) || steps.Any(s => currentUser.IsInRole(s.RoleCode)))
                {
                    flows[version.Id] = new ConfirmationFlow(steps, template.HospitalSignerRoleCode);
                }
            }
        }

        if (flows.Count == 0)
        {
            return new DashboardFormList(0, []);
        }

        var waiting = await statistics.ListAwaitingConfirmationAsync(scope, [.. flows.Keys], cancellationToken).ConfigureAwait(false);
        var mine = waiting
            .Where(form => flows.TryGetValue(form.TemplateVersionId, out var flow) && IsMyTurn(form, flow))
            .OrderBy(form => form.PatientSignedAtUtc)
            .ThenBy(form => form.FormId)
            .Select(form => form.FormId)
            .ToList();
        var shown = mine.Take(ListSize).ToList();
        var items = (await statistics.ListAwaitingConfirmationItemsAsync(shown, cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.Id);
        return new DashboardFormList(mine.Count, [.. shown.Where(items.ContainsKey).Select(id => items[id])]);
    }

    private bool IsMyTurn(AwaitingConfirmationForm form, ConfirmationFlow flow)
    {
        var next = ConfirmationTurn.NextStep(flow.Steps, form.Steps.Select(s => s.StepKey));
        var (canConfirmStep, canConfirm) = ConfirmationTurn.For(
            currentUser,
            awaitingConfirmation: true,
            next?.RoleCode,
            flow.FinalRoleCode,
            confirmedAStep: form.Steps.Any(s => s.UserId == currentUser.UserId));
        return canConfirmStep || canConfirm;
    }

    /// <summary>Các bước trung gian của một phiên bản mẫu và vai trò xác nhận cuối của mẫu.</summary>
    private sealed record ConfirmationFlow(IReadOnlyList<TemplateConfirmationStep> Steps, string FinalRoleCode);
}
