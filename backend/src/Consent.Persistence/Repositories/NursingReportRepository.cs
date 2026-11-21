using Consent.Application.Abstractions;
using Consent.Application.NursingReports;
using Consent.Domain.NursingReports;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

internal sealed class NursingReportRepository(ConsentDbContext db) : INursingReportRepository
{
    public Task<NursingDailyReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.NursingDailyReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<NursingDailyReport?> FindAsync(Guid departmentId, DateOnly reportDate, CancellationToken cancellationToken) =>
        db.NursingDailyReports.FirstOrDefaultAsync(r => r.DepartmentId == departmentId && r.ReportDate == reportDate, cancellationToken);

    public Task<NursingDailyReport?> FindLatestSubmittedBeforeAsync(Guid departmentId, DateOnly before, CancellationToken cancellationToken) =>
        db.NursingDailyReports
            .Where(r => r.DepartmentId == departmentId && r.ReportDate < before && r.Status == NursingReportStatus.Submitted)
            .OrderByDescending(r => r.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<NursingDailyReport>> ListByDateAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        await db.NursingDailyReports.Where(r => r.ReportDate == reportDate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<NursingSubmittedFigures>> ListSubmittedFiguresAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.NursingDailyReports
            .Where(r => r.ReportDate >= from && r.ReportDate <= to && r.Status == NursingReportStatus.Submitted)
            // So với null không qua bộ mã hóa: SQL chỉ kiểm tra cột NotesEncrypted có giá trị, không giải mã nội dung.
            .Select(r => new NursingSubmittedFigures(r.DepartmentId, r.ReportDate, r.FiguresJson, r.Notes != null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(NursingDailyReport report) => db.NursingDailyReports.Add(report);

    public async Task<IReadOnlyList<NursingReportDepartment>> ListDepartmentsAsync(CancellationToken cancellationToken) =>
        await db.NursingReportDepartments.ToListAsync(cancellationToken).ConfigureAwait(false);

    public void AddDepartment(NursingReportDepartment department) => db.NursingReportDepartments.Add(department);

    public void RemoveDepartment(NursingReportDepartment department) => db.NursingReportDepartments.Remove(department);

    public Task<NursingDailyStatistics?> FindStatisticsAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        db.NursingDailyStatistics.FirstOrDefaultAsync(s => s.ReportDate == reportDate, cancellationToken);

    public void AddStatistics(NursingDailyStatistics statistics) => db.NursingDailyStatistics.Add(statistics);

    public async Task<IReadOnlyList<NursingDepartmentStatistics>> ListDepartmentStatisticsAsync(
        DateOnly reportDate, CancellationToken cancellationToken) =>
        await db.NursingDepartmentStatistics.Where(s => s.ReportDate == reportDate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void AddDepartmentStatistics(NursingDepartmentStatistics statistics) => db.NursingDepartmentStatistics.Add(statistics);

    public void AddDelegation(NursingReportDelegation delegation) => db.NursingReportDelegations.Add(delegation);

    public Task<NursingReportDelegation?> GetDelegationAsync(Guid id, CancellationToken cancellationToken) =>
        db.NursingReportDelegations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <summary>Tối đa 300 ủy quyền mới nhất (đủ cho màn hình danh sách).</summary>
    public async Task<IReadOnlyList<NursingReportDelegation>> ListDelegationsAsync(Guid? userId, CancellationToken cancellationToken) =>
        await db.NursingReportDelegations
            .Where(d => userId == null || d.DelegatorUserId == userId || d.DelegateUserId == userId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(300)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<NursingReportDelegation>> ListDelegationsCoveringAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.NursingReportDelegations
            .Where(d => d.RevokedAtUtc == null && d.StartDate <= date && d.EndDate >= date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<NursingReportDelegation>> GetDelegationsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0
            ? []
            : await db.NursingReportDelegations.Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
