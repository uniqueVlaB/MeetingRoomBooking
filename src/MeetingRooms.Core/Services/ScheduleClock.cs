using MeetingRooms.Core.Options;
using Microsoft.Extensions.Options;

namespace MeetingRooms.Core.Services;

/// <summary>
/// Answers "what day is it?" for the schedule.
/// </summary>
/// <remarks>
/// Slots carry local wall-clock times and bookings a local calendar date, so every date decision —
/// the "no booking in the past" rule, and the date a schedule defaults to — has to be made in the
/// schedule's own time zone. Both used to compute it from the server's UTC date independently,
/// which made them wrong together in any zone that is not UTC. One type, injected in both places,
/// is what keeps them agreeing.
/// </remarks>
/// <param name="timeProvider">Supplies the current instant, so date rules are testable.</param>
/// <param name="options">Supplies the schedule's time zone.</param>
public sealed class ScheduleClock(TimeProvider timeProvider, IOptions<BookingOptions> options)
{
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly TimeZoneInfo timeZone = ResolveTimeZone(options.Value.TimeZone);

    /// <summary>The current date in the schedule's time zone.</summary>
    /// <returns>Today, as a schedule reader would name it.</returns>
    public DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(this.timeProvider.GetUtcNow(), this.timeZone).DateTime);

    /// <summary>The current instant, for stamping rows.</summary>
    /// <returns>The current UTC time.</returns>
    public DateTimeOffset UtcNow() => this.timeProvider.GetUtcNow();

    /// <summary>Resolves a configured time-zone identifier.</summary>
    /// <param name="identifier">An IANA or Windows time-zone identifier.</param>
    /// <returns>The matching time zone.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the identifier is not one this machine knows. Failing at start-up is deliberate:
    /// silently falling back to UTC would move every date boundary without anything to notice it by.
    /// </exception>
    private static TimeZoneInfo ResolveTimeZone(string identifier)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(identifier);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"'{identifier}' is not a time zone this machine recognises. Set " +
                $"'{BookingOptions.SectionName}:{nameof(BookingOptions.TimeZone)}' to an IANA " +
                "identifier such as 'Europe/Kyiv', or to 'UTC'.",
                exception);
        }
    }
}
