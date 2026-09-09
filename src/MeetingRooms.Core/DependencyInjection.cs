using FluentValidation;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Services;
using MeetingRooms.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddValidatorsFromAssemblyContaining<CreateRoomRequestValidator>();

        // Injected rather than read from DateTime.UtcNow so that date rules -- "no booking in the
        // past" -- can be tested without waiting for midnight.
        services.TryAddSingletonTimeProvider();

        return services;
    }

    /// <summary>Registers the system clock unless a test has already substituted one.</summary>
    /// <param name="services">The service collection.</param>
    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
