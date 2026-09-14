using FluentValidation;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Services;
using MeetingRooms.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingRooms.Core;

/// <summary>Registers the domain services.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the booking and room services and their validators.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddValidatorsFromAssemblyContaining<CreateRoomRequestValidator>();

        // Injected rather than read from DateTime.UtcNow so that date rules -- "no booking in the
        // past" -- can be tested without waiting for midnight. TryAdd so a test that has already
        // substituted a fake clock keeps it.
        services.TryAddSingleton(TimeProvider.System);

        // Stateless over the clock and the configured time zone, so a singleton.
        services.TryAddSingleton<ScheduleClock>();

        return services;
    }
}
