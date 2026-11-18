using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using TestSupport;

namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>Một container SQL Server dùng chung cho mọi test cần cơ sở dữ liệu; mỗi test dùng database riêng.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public async Task InitializeAsync() =>
        _container = await SqlServerContainers.StartAsync(() => new MsSqlBuilder(TestSettings.SqlServerImage)
            // Bản Standard (như máy chủ của bệnh viện) để không vô tình dùng tính năng chỉ có ở Enterprise.
            .WithEnvironment("MSSQL_PID", "StandardDeveloper")
            .Build());

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public string CreateConnectionString(string databaseName)
    {
        var container = _container ?? throw new InvalidOperationException("SQL Server thử nghiệm chưa khởi động.");
        return new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
