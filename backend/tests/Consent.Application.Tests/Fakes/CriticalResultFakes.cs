using Consent.Application.CriticalResults;
using Consent.Domain.CriticalResults;

namespace Consent.Application.Tests.Fakes;

/// <summary>Kho hồ sơ và danh mục kết quả nguy cấp trong bộ nhớ (quyết định 60), lọc như kho thật.</summary>
internal sealed class InMemoryCriticalResultRepository(InMemoryStore store) : ICriticalResultRepository
{
    public void Add(CriticalTest test) => store.CriticalTests.Add(test);

    public void Add(CriticalResult result) => store.CriticalResults.Add(result);

    public Task<IReadOnlyList<CriticalTest>> ListTestsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriticalTest>>([.. store.CriticalTests]);

    public Task<CriticalTest?> GetTestAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.CriticalTests.Find(t => t.Id == id));

    public Task<CriticalResult?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.CriticalResults.Find(r => r.Id == id));

    public Task<IReadOnlyList<CriticalResult>> ListSentAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriticalResult>>([.. store.CriticalResults
            .Where(r => departmentIds is null || departmentIds.Contains(r.SendingDepartmentId))
            .Where(r => OpenOrSince(r, closedSinceUtc)
                || (r.Status == CriticalResultStatus.Completed
                    && r.Notifications.Any(n => n.Method == CriticalNotificationMethod.App && n.RejectedAtUtc is null)))]);

    public Task<IReadOnlyList<CriticalResult>> ListReceivedAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriticalResult>>([.. store.CriticalResults
            .Where(r => r.ReceivingDepartmentId is { } receiving && (departmentIds is null || departmentIds.Contains(receiving)))
            .Where(r => OpenOrSince(r, closedSinceUtc))]);

    public Task<IReadOnlyList<CriticalResult>> ListByResultTimeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriticalResult>>([.. store.CriticalResults.Where(r => r.ResultAtUtc >= fromUtc && r.ResultAtUtc < toUtc)]);

    private static bool OpenOrSince(CriticalResult r, DateTimeOffset closedSinceUtc) =>
        r.Status is not (CriticalResultStatus.Completed or CriticalResultStatus.Cancelled) || r.UpdatedAtUtc >= closedSinceUtc;
}

/// <summary>Số hồ sơ tăng dần như sequence của SQL Server.</summary>
internal sealed class SequentialCriticalNumbers : ICriticalResultNumberGenerator
{
    private int _next;

    public Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult($"KQNC-{nowUtc.ToOffset(TimeSpan.FromHours(7)).Year}-{++_next:000000}");
}

/// <summary>Giữ lại dữ liệu dựng PDF sổ kết quả nguy cấp để test kiểm tra.</summary>
internal sealed class CapturingCriticalLogbookPdf : ICriticalLogbookPdfRenderer
{
    public CriticalLogbookDocument? Document { get; private set; }

    public byte[] Render(CriticalLogbookDocument document)
    {
        Document = document;
        return "%PDF-giả"u8.ToArray();
    }
}
