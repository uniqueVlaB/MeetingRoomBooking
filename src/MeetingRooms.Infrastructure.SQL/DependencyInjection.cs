using MeetingRooms.Core.Abstractions;
using MeetingRooms.Infrastructure.SQL.Concurrency;
using MeetingRooms.Infrastructure.SQL.Database;
using MeetingRooms.Infrastructure.SQL.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeetingRooms.Infrastructure.SQL;

/// <summary>Registers SQL Server persistence.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// The Aspire resource name for the database. The same string names the connection string in
    /// Azure (<c>ConnectionStrings__meetingrooms-db</c>), so there is one name to keep in sync
    /// rather than one per environment.
    /// </summary>
    public const string DatabaseResourceName = "meetingrooms-db";

    /// <summary>
    /// Adds the DbContext, the unit of work and the slot-conflict detector.
    /// </summary>
    /// <remarks>
    /// Takes the host builder rather than <c>IServiceCollection</c> because the Aspire integration
    /// resolves the connection string, health check and telemetry from configuration itself.
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostApplicationBuilder AddSqlInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Registers AppDbContext, a health check and EF Core telemetry, reading the connection
        // string that the AppHost (locally) or Web App settings (in Azure) provide under this name.
        // Retries on transient failures are on by default, which matters against Azure SQL.
        builder.AddSqlServerDbContext<AppDbContext>(DatabaseResourceName);

        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Stateless, so a singleton; it only inspects exceptions.
        builder.Services.AddSingleton<ISlotConflictDetector, SqlServerSlotConflictDetector>();

        return builder;
    }
}
