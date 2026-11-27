using Consent.Application.Abstractions;
using Consent.Domain.QualityControl;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

/// <summary>Sổ nội kiểm đường huyết (quyết định 57): danh mục và sổ tháng (nạp kèm kết quả, lần xem xét).</summary>
internal sealed class QualityControlRepository(ConsentDbContext db) : IQualityControlRepository
{
    public void Add(QcMeter meter) => db.QcMeters.Add(meter);

    public void Add(QcLot lot) => db.QcLots.Add(lot);

    public void Add(QcBook book) => db.QcBooks.Add(book);

    public async Task<IReadOnlyList<QcMeter>> ListMetersAsync(CancellationToken cancellationToken) =>
        await db.QcMeters.ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<QcMeter?> GetMeterAsync(Guid id, CancellationToken cancellationToken) =>
        db.QcMeters.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<bool> SerialExistsAsync(string serialNumber, Guid? excludeId, CancellationToken cancellationToken) =>
        db.QcMeters.AnyAsync(m => m.SerialNumber == serialNumber && (excludeId == null || m.Id != excludeId), cancellationToken);

    public async Task<IReadOnlyList<QcLot>> ListLotsAsync(CancellationToken cancellationToken) =>
        await db.QcLots.ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<QcLot?> GetLotAsync(Guid id, CancellationToken cancellationToken) =>
        db.QcLots.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<bool> LotExistsAsync(QcLotKind kind, string lotNumber, int? level, Guid? excludeId, CancellationToken cancellationToken) =>
        db.QcLots.AnyAsync(
            l => l.Kind == kind && l.LotNumber == lotNumber && l.Level == level && (excludeId == null || l.Id != excludeId),
            cancellationToken);

    public Task<QcBook?> GetBookAsync(Guid id, CancellationToken cancellationToken) =>
        Books().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<QcBook?> FindBookAsync(Guid meterId, Guid departmentId, int year, int month, CancellationToken cancellationToken) =>
        Books().FirstOrDefaultAsync(
            b => b.MeterId == meterId && b.DepartmentId == departmentId && b.Year == year && b.Month == month, cancellationToken);

    public async Task<IReadOnlyList<QcBook>> ListBooksAsync(int year, int month, CancellationToken cancellationToken) =>
        await Books().Where(b => b.Year == year && b.Month == month).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<QcRun?> FindLatestRunAsync(Guid meterId, CancellationToken cancellationToken) =>
        db.Set<QcRun>()
            .Where(r => r.CancelledAtUtc == null && db.QcBooks.Any(b => b.Id == r.BookId && b.MeterId == meterId))
            .OrderByDescending(r => r.RunDate)
            .ThenByDescending(r => r.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<QcBook> Books() =>
        db.QcBooks.Include(b => b.Runs).Include(b => b.Reviews).AsSplitQuery();
}
