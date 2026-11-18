using Consent.Application.Abstractions;
using Consent.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Consent.Persistence.Auditing;

/// <summary>Cập nhật chỉ mục mù <c>CitizenIdHash</c> mỗi khi số CCCD (giá trị gốc trong bộ nhớ) thay đổi.</summary>
internal sealed class BlindIndexInterceptor(IBlindIndexer blindIndexer) : SaveChangesInterceptor
{
    private const string CitizenId = "CitizenId";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateBlindIndexes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateBlindIndexes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateBlindIndexes(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Metadata.FindProperty(PatientConfiguration.CitizenIdHash) is null)
            {
                continue;
            }

            var citizenId = entry.Property(CitizenId);
            if (entry.State == EntityState.Added || citizenId.IsModified)
            {
                entry.Property(PatientConfiguration.CitizenIdHash).CurrentValue =
                    citizenId.CurrentValue is string value ? blindIndexer.Compute(value) : null;
            }
        }
    }
}
