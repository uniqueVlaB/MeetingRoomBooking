using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>
/// Applies every entity configuration.
/// </summary>
/// <remarks>
/// Registered explicitly rather than through <c>ApplyConfigurationsFromAssembly</c>: a configuration
/// that is added but never wired up would otherwise fail silently, taking the active-slot index with
/// it. Here, forgetting a line is visible in this file.
/// </remarks>
[ExcludeFromCodeCoverage]
public static class EntityConfigurator
{
    /// <summary>Applies all configurations to the model.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    public static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ApplicationUserConfiguration());
        modelBuilder.ApplyConfiguration(new RoomConfiguration());
        modelBuilder.ApplyConfiguration(new TimeSlotConfiguration());
        modelBuilder.ApplyConfiguration(new BookingConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
