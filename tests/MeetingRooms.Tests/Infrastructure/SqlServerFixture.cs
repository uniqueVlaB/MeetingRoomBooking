using MeetingRooms.Infrastructure.SQL.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace MeetingRooms.Tests.Infrastructure;

/// <summary>
/// Provides a real SQL Server database for the test run.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour under test is a database guarantee — a filtered unique index and the specific
/// errors it raises — so the in-memory and SQLite providers would pass while proving nothing at all.
/// </para>
/// <para>
/// By default the fixture starts a throwaway SQL Server container through Testcontainers, which
/// needs Docker. A reviewer without Docker can point the tests at any SQL Server instead by setting
/// <c>MEETINGROOMS_TEST_SQL</c> to a server connection string, for example
/// <c>Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True</c>. Either
/// way the fixture creates its own uniquely named database and drops it afterwards, so it never
/// touches existing data.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>Environment variable that redirects the tests to an existing SQL Server.</summary>
    public const string ConnectionStringVariable = "MEETINGROOMS_TEST_SQL";

    /// <summary>
    /// SQL Server image for the throwaway container. Pinned rather than floating on <c>:latest</c>,
    /// so a run that is green today is still green tomorrow.
    /// </summary>
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? container;

    /// <summary>Connection string for the database created for this run.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Creates the database and applies all migrations.</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    public async Task InitializeAsync()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            this.container = new MsSqlBuilder(SqlServerImage).Build();
            await this.container.StartAsync();
            serverConnectionString = this.container.GetConnectionString();
        }

        // Always work in a database of our own, whatever server we were pointed at.
        this.ConnectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"MeetingRooms_Tests_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using var dbContext = this.CreateDbContext();

        // Migrate rather than EnsureCreated: the filtered unique index must arrive through the same
        // migration that production uses, or the tests would prove a schema nobody deploys.
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>Drops the database and stops the container, if one was started.</summary>
    /// <returns>A task that completes when everything is cleaned up.</returns>
    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(this.ConnectionString))
        {
            await using var dbContext = this.CreateDbContext();
            await dbContext.Database.EnsureDeletedAsync();
        }

        if (this.container is not null)
        {
            await this.container.DisposeAsync();
        }
    }

    /// <summary>Creates a context against the test database.</summary>
    /// <returns>A new <see cref="AppDbContext"/>; the caller disposes it.</returns>
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(this.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}

/// <summary>
/// Shares one <see cref="SqlServerFixture"/> across every test class, so the container starts once
/// per run rather than once per class — and so nothing else is hammering the database while the
/// concurrency test measures contention.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    /// <summary>Name used by <c>[Collection]</c> on the test classes.</summary>
    public const string Name = "SqlServer";
}
