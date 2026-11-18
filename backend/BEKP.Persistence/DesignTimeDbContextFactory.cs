using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BEKP.Persistence;

/// <summary>Used by "dotnet ef" only. Reads BEKP_DESIGN_CONNECTION; the placeholder never connects when adding migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BekpDbContext>
{
    public BekpDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("BEKP_DESIGN_CONNECTION")
            ?? "Server=localhost;Database=BEKP;Integrated Security=false;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<BekpDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", BekpDbContext.Schema))
            .Options;
        return new BekpDbContext(options);
    }
}
