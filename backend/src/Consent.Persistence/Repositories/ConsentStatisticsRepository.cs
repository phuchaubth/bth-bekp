using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.Reports;
using Consent.Domain.ConsentForms;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

/// <summary>
/// Thống kê phiếu: đếm và nhóm trong SQL Server, chỉ dùng các cột không mã hóa (khoa, mẫu, trạng thái, thời điểm).
/// Ngày được tính bằng số giờ kể từ mốc 00:00 giờ Việt Nam chia 24 (Việt Nam không đổi giờ theo mùa).
/// </summary>
internal sealed class ConsentStatisticsRepository(ConsentDbContext db) : IConsentStatisticsRepository
{
    private const int HoursPerDay = 24;

    public async Task<IReadOnlyList<ConsentFormGroupCount>> CountByGroupAsync(ConsentFormSearch query, CancellationToken cancellationToken) =>
        await ConsentFormFilters.Apply(db, query)
            .GroupBy(f => new { f.DepartmentId, f.TemplateId, f.Status, f.Decision, f.SignerKind })
            .Select(g => new ConsentFormGroupCount(
                g.Key.DepartmentId, g.Key.TemplateId, g.Key.Status, g.Key.Decision, g.Key.SignerKind, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<DailyStatusCount>> CountByCreatedDayAsync(
        ConsentFormSearch query, DateTimeOffset originUtc, CancellationToken cancellationToken) =>
        await ConsentFormFilters.Apply(db, query)
            .Where(f => f.CreatedAtUtc >= originUtc)
            .Select(f => new { Day = EF.Functions.DateDiffHour(originUtc, f.CreatedAtUtc) / HoursPerDay, f.Status })
            .GroupBy(x => new { x.Day, x.Status })
            .Select(g => new DailyStatusCount(g.Key.Day, g.Key.Status, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<DailyCount>> CountCompletedByDayAsync(
        ConsentFormSearch scope, DateTimeOffset originUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        await ConsentFormFilters.Apply(db, scope)
            .Where(f => f.CompletedAtUtc >= originUtc && f.CompletedAtUtc < toUtc)
            .Select(f => EF.Functions.DateDiffHour(originUtc, f.CompletedAtUtc!.Value) / HoursPerDay)
            .GroupBy(day => day)
            .Select(g => new DailyCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<int>> CompletionSecondsAsync(ConsentFormSearch query, CancellationToken cancellationToken) =>
        await ConsentFormFilters.Apply(db, query)
            .Where(f => f.Status == ConsentFormStatus.Completed && f.CompletedAtUtc != null)
            .Select(f => EF.Functions.DateDiffSecond(f.CreatedAtUtc, f.CompletedAtUtc!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountClosedAsync(
        ConsentFormSearch scope, bool revoked, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var forms = ConsentFormFilters.Apply(db, scope);
        return revoked
            ? forms.CountAsync(f => f.RevokedAtUtc >= fromUtc && f.RevokedAtUtc < toUtc, cancellationToken)
            : forms.CountAsync(f => f.CompletedAtUtc >= fromUtc && f.CompletedAtUtc < toUtc, cancellationToken);
    }

    public async Task<DashboardFormList> AwaitingConfirmationAsync(
        ConsentFormSearch scope, IReadOnlyCollection<Guid> templateIds, int take, CancellationToken cancellationToken)
    {
        var forms = ConsentFormFilters.Apply(db, scope)
            .Where(f => f.Status == ConsentFormStatus.PendingSignature
                        && templateIds.Contains(f.TemplateId)
                        && f.Signatures.Any(s => (s.Role == ConsentSignerRole.Patient || s.Role == ConsentSignerRole.Representative)));
        var total = await forms.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await (
                from f in forms
                join p in db.Patients on f.PatientId equals p.Id
                join t in db.ConsentTemplates on f.TemplateId equals t.Id
                join d in db.Departments on f.DepartmentId equals d.Id
                let signedAt = f.Signatures.Where(s => (s.Role == ConsentSignerRole.Patient || s.Role == ConsentSignerRole.Representative)).Max(s => (DateTimeOffset?)s.SignedAtUtc)
                    ?? f.UpdatedAtUtc
                orderby signedAt
                select new DashboardFormItem(f.Id, f.FormNo, f.Status, p.FullName, t.Code, t.Name, d.Name, signedAt))
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new DashboardFormList(total, items);
    }

    public async Task<DashboardFormList> OpenFormsCreatedByAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        var forms = db.ConsentForms.AsNoTracking()
            .Where(f => f.CreatedByUserId == userId
                        && (f.Status == ConsentFormStatus.Draft || f.Status == ConsentFormStatus.PendingSignature));
        var total = await forms.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await (
                from f in forms
                join p in db.Patients on f.PatientId equals p.Id
                join t in db.ConsentTemplates on f.TemplateId equals t.Id
                join d in db.Departments on f.DepartmentId equals d.Id
                orderby f.UpdatedAtUtc descending
                select new DashboardFormItem(f.Id, f.FormNo, f.Status, p.FullName, t.Code, t.Name, d.Name, f.UpdatedAtUtc))
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new DashboardFormList(total, items);
    }
}
