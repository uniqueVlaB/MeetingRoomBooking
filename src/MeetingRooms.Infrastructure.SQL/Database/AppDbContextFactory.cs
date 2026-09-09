using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeetingRooms.Infrastructure.SQL.Database;

/// <summary>
/// Builds an <see cref="AppDbContext"/> for the <c>dotnet ef</c> tools at design time.
/// </summary>
/// <remarks>
/// At runtime the connection string is supplied by Aspire (locally) or by Web App configuration (in
/// Azure), neither of which exists while running <c>dotnet ef migrations add</c>. This factory gives
/// the tools a target without requiring the API project to be startable, so adding a migration never
/// depends on the rest of the application being configured.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>Environment variable that overrides the design-time connection string.</summary>
    public const string ConnectionStringVariable = "MEETINGROOMS_DESIGN_TIME_SQL";

    /// <summary>
    /// LocalDB by default: migrations only need a server that can be reasoned about, and this one
    /// requires no setup on a Windows developer machine.
    /// </summary>
    private const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=MeetingRooms_DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
