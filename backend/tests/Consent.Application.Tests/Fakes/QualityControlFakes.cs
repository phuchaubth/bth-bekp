using Consent.Application.Abstractions;
using Consent.Application.QualityControl;
using Consent.Domain.QualityControl;

namespace Consent.Application.Tests.Fakes;

/// <summary>Kho sổ nội kiểm trong bộ nhớ (quyết định 57).</summary>
internal sealed class InMemoryQualityControlRepository(InMemoryStore store) : IQualityControlRepository
{
    public void Add(QcMeter meter) => store.QcMeters.Add(meter);

    public void Add(QcLot lot) => store.QcLots.Add(lot);

    public void Add(QcBook book) => store.QcBooks.Add(book);

    public Task<IReadOnlyList<QcMeter>> ListMetersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QcMeter>>([.. store.QcMeters]);

    public Task<QcMeter?> GetMeterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcMeters.Find(m => m.Id == id));

    public Task<bool> SerialExistsAsync(string serialNumber, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcMeters.Exists(
            m => string.Equals(m.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase) && m.Id != excludeId));

    public Task<IReadOnlyList<QcLot>> ListLotsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QcLot>>([.. store.QcLots]);

    public Task<QcLot?> GetLotAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcLots.Find(l => l.Id == id));

    public Task<bool> LotExistsAsync(QcLotKind kind, string lotNumber, int? level, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcLots.Exists(l =>
            l.Kind == kind && string.Equals(l.LotNumber, lotNumber, StringComparison.OrdinalIgnoreCase) && l.Level == level && l.Id != excludeId));

    public Task<QcBook?> GetBookAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcBooks.Find(b => b.Id == id));

    public Task<QcBook?> FindBookAsync(Guid meterId, Guid departmentId, int year, int month, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcBooks.Find(b => b.MeterId == meterId && b.DepartmentId == departmentId && b.Year == year && b.Month == month));

    public Task<IReadOnlyList<QcBook>> ListBooksAsync(int year, int month, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QcBook>>([.. store.QcBooks.Where(b => b.Year == year && b.Month == month)]);

    public Task<QcRun?> FindLatestRunAsync(Guid meterId, CancellationToken cancellationToken) =>
        Task.FromResult(store.QcBooks
            .Where(b => b.MeterId == meterId)
            .SelectMany(b => b.Runs)
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.RunDate)
            .ThenByDescending(r => r.RecordedAtUtc)
            .FirstOrDefault());
}

/// <summary>Giữ lại dữ liệu dựng PDF sổ nội kiểm để test kiểm tra.</summary>
internal sealed class CapturingQcLogPdf : IQcLogPdfRenderer
{
    public QcLogPdfDocument? Document { get; private set; }

    public byte[] Render(QcLogPdfDocument document)
    {
        Document = document;
        return "%PDF-giả"u8.ToArray();
    }
}
