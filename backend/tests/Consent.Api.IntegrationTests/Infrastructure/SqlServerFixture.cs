using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>Một container SQL Server dùng chung cho mọi test cần cơ sở dữ liệu; mỗi test dùng database riêng.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(TestSettings.SqlServerImage).Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string CreateConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
