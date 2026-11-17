using Consent.Application.Abstractions;
using Consent.Application.NursingReports;
using Consent.Domain.NursingReports;

namespace Consent.Application.Tests.Fakes;

internal sealed class InMemoryNursingReportRepository(InMemoryStore store) : INursingReportRepository
{
    public Task<NursingDailyReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.NursingReports.Find(r => r.Id == id));

    public Task<NursingDailyReport?> FindAsync(Guid departmentId, DateOnly reportDate, CancellationToken cancellationToken) =>
        Task.FromResult(store.NursingReports.Find(r => r.DepartmentId == departmentId && r.ReportDate == reportDate));

    public Task<NursingDailyReport?> FindLatestSubmittedBeforeAsync(Guid departmentId, DateOnly before, CancellationToken cancellationToken) =>
        Task.FromResult(store.NursingReports
            .Where(r => r.DepartmentId == departmentId && r.ReportDate < before && r.IsSubmitted)
            .OrderByDescending(r => r.ReportDate)
            .FirstOrDefault());

    public Task<IReadOnlyList<NursingDailyReport>> ListByDateAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NursingDailyReport>>([.. store.NursingReports.Where(r => r.ReportDate == reportDate)]);

    public void Add(NursingDailyReport report) => store.NursingReports.Add(report);

    public Task<IReadOnlyList<NursingReportDepartment>> ListDepartmentsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NursingReportDepartment>>([.. store.NursingDepartments]);

    public void AddDepartment(NursingReportDepartment department) => store.NursingDepartments.Add(department);

    public void RemoveDepartment(NursingReportDepartment department) => store.NursingDepartments.Remove(department);

    public Task<NursingDailyStatistics?> FindStatisticsAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        Task.FromResult(store.NursingStatistics.Find(s => s.ReportDate == reportDate));

    public void AddStatistics(NursingDailyStatistics statistics) => store.NursingStatistics.Add(statistics);

    public Task<IReadOnlyList<NursingDepartmentStatistics>> ListDepartmentStatisticsAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NursingDepartmentStatistics>>([.. store.NursingDepartmentStatistics.Where(s => s.ReportDate == reportDate)]);

    public void AddDepartmentStatistics(NursingDepartmentStatistics statistics) => store.NursingDepartmentStatistics.Add(statistics);
}

internal sealed class StubDailyStatisticsSource : IDailyStatisticsSource
{
    public bool IsEnabled { get; set; }

    public DailyStatisticsResult Result { get; set; } = DailyStatisticsResult.Unavailable;

    public List<DateOnly> Calls { get; } = [];

    public Task<DailyStatisticsResult> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        Calls.Add(date);
        return Task.FromResult(Result);
    }
}

/// <summary>Ghi lại dữ liệu đưa vào PDF/Excel để test kiểm tra; nội dung file là vài byte giả.</summary>
internal sealed class CapturingNursingDocuments : INursingReportPdfRenderer, INursingSummaryWorkbookWriter
{
    public NursingReportPdfDocument? Report { get; private set; }

    public NursingSummaryDocument? Summary { get; private set; }

    public NursingSummaryDocument? Workbook { get; private set; }

    public byte[] RenderReport(NursingReportPdfDocument document)
    {
        Report = document;
        return [0x25, 0x50, 0x44, 0x46];
    }

    public byte[] RenderSummary(NursingSummaryDocument document)
    {
        Summary = document;
        return [0x25, 0x50, 0x44, 0x46];
    }

    public byte[] Write(NursingSummaryDocument document)
    {
        Workbook = document;
        return [0x50, 0x4B, 0x03, 0x04];
    }
}
