using Consent.Application.ConsentForms;
using Consent.Application.CriticalResults;
using Consent.Domain.CriticalResults;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

/// <summary>Hồ sơ và danh mục kết quả nguy cấp (quyết định 60); hồ sơ nạp kèm các lần báo.</summary>
internal sealed class CriticalResultRepository(ConsentDbContext db) : ICriticalResultRepository
{
    public void Add(CriticalTest test) => db.CriticalTests.Add(test);

    public void Add(CriticalResult result) => db.CriticalResults.Add(result);

    public async Task<IReadOnlyList<CriticalTest>> ListTestsAsync(CancellationToken cancellationToken) =>
        await db.CriticalTests.ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<CriticalTest?> GetTestAsync(Guid id, CancellationToken cancellationToken) =>
        db.CriticalTests.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<CriticalResult?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Results().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CriticalResult>> ListSentAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken)
    {
        var query = Results();
        if (departmentIds is not null)
        {
            var ids = departmentIds.ToArray();
            query = query.Where(r => ids.Contains(r.SendingDepartmentId));
        }

        return await OpenOrSince(query, closedSinceUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CriticalResult>> ListReceivedAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken)
    {
        var query = Results().Where(r => r.ReceivingDepartmentId != null);
        if (departmentIds is not null)
        {
            var ids = departmentIds.Select(id => (Guid?)id).ToArray();
            query = query.Where(r => ids.Contains(r.ReceivingDepartmentId));
        }

        return await OpenOrSince(query, closedSinceUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CriticalResult>> ListByResultTimeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        await Results()
            .Where(r => r.ResultAtUtc >= fromUtc && r.ResultAtUtc < toUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static IQueryable<CriticalResult> OpenOrSince(IQueryable<CriticalResult> query, DateTimeOffset closedSinceUtc) =>
        query.Where(r =>
            (r.Status != CriticalResultStatus.Completed && r.Status != CriticalResultStatus.Cancelled) || r.UpdatedAtUtc >= closedSinceUtc);

    private IQueryable<CriticalResult> Results() => db.CriticalResults.Include(r => r.Notifications).AsSplitQuery();
}

/// <summary>Số hồ sơ <c>KQNC-{năm}-{số thứ tự}</c>, số thứ tự lấy từ sequence của SQL Server.</summary>
internal sealed class CriticalResultNumberGenerator(ConsentDbContext db) : ICriticalResultNumberGenerator
{
    public const string SequenceName = "CriticalResultNumbers";

    public async Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // Như số phiếu: không ghép thêm toán tử LINQ, SQL Server không cho NEXT VALUE FOR trong truy vấn con.
        var values = await db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR dbo.CriticalResultNumbers AS [Value]")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var year = nowUtc.ToOffset(ConsentFormService.VietnamOffset).Year;
        return $"KQNC-{year}-{values.Single():000000}";
    }
}
