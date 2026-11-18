using BEKP.Application.Abstractions;
using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;
using Microsoft.EntityFrameworkCore;

namespace BEKP.Persistence;

/// <summary>EF Core context for the BEKP database (SQL Server, schema "bekp").</summary>
public sealed class BekpDbContext(DbContextOptions<BekpDbContext> options) : DbContext(options), IUnitOfWork
{
    public const string Schema = "bekp";

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Bed> Beds => Set<Bed>();

    public DbSet<InpatientStay> InpatientStays => Set<InpatientStay>();

    public DbSet<FebrileNeutropeniaEpisode> FebrileNeutropeniaEpisodes => Set<FebrileNeutropeniaEpisode>();

    public DbSet<BloodRequest> BloodRequests => Set<BloodRequest>();

    public DbSet<BloodInventoryItem> BloodInventory => Set<BloodInventoryItem>();

    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();

    public DbSet<ProcessTiming> ProcessTimings => Set<ProcessTiming>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BekpDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // All timestamps are stored and read back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
