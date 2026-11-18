using Consent.Application.Abstractions;
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
}
