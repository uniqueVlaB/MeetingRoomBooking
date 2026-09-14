using MeetingRooms.Core.Options;
using MeetingRooms.Core.Services;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace MeetingRooms.Tests.Domain;

/// <summary>
/// Which day the schedule considers "today".
/// </summary>
/// <remarks>
/// Slots carry local wall-clock times and bookings a local date, so the "no booking in the past"
/// rule is only meaningful against a time zone. Deciding it from the server's UTC date instead is
/// wrong for roughly two hours of every day in Kyiv and considerably longer further west, and the
/// symptom — being refused a slot on your own current afternoon — looks like a broken booking
/// system rather than a clock problem. These pin the boundary.
/// </remarks>
public sealed class ScheduleClockTests
{
    /// <summary>
    /// Late evening in Kyiv is already tomorrow in UTC, and the clock must still say today.
    /// </summary>
    /// <remarks>
    /// 22:30 UTC on 14 September is 01:30 on the 15th in Kyiv (UTC+3 in summer). A user opening the
    /// schedule then is looking at the 15th, and booking it must not be refused as "in the past".
    /// </remarks>
    [Fact]
    public void AfterUtcMidnightInTheScheduleZone_TodayIsTheLocalDay()
    {
        var clock = NewClock("Europe/Kyiv", new DateTimeOffset(2026, 9, 14, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 15), clock.Today());
    }

    /// <summary>
    /// Before UTC midnight but after local midnight, the local day is still the answer.
    /// </summary>
    /// <remarks>
    /// The mirror case, and the one a UTC-only rule gets wrong in the other direction: 00:30 UTC on
    /// the 15th is still 03:30 on the 15th in Kyiv, so both agree here. Included so the pair
    /// documents that the zone — not an offset applied in one direction — is what decides.
    /// </remarks>
    [Fact]
    public void EarlyUtcMorning_IsTheSameLocalDay()
    {
        var clock = NewClock("Europe/Kyiv", new DateTimeOffset(2026, 9, 15, 0, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 15), clock.Today());
    }

    /// <summary>A zone behind UTC keeps the earlier day while UTC has already rolled over.</summary>
    [Fact]
    public void InAZoneBehindUtc_TodayIsStillTheEarlierDay()
    {
        var clock = NewClock("America/New_York", new DateTimeOffset(2026, 9, 15, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 14), clock.Today());
    }

    /// <summary>Configured as UTC, the clock simply reports the UTC date.</summary>
    [Fact]
    public void ConfiguredAsUtc_TodayIsTheUtcDay()
    {
        var clock = NewClock("UTC", new DateTimeOffset(2026, 9, 14, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 14), clock.Today());
    }

    /// <summary>
    /// An unknown time zone fails loudly.
    /// </summary>
    /// <remarks>
    /// Falling back to UTC would move every date boundary silently, which is the failure this whole
    /// type exists to prevent. Better to refuse to start.
    /// </remarks>
    [Fact]
    public void AnUnrecognisedTimeZone_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NewClock("Middle/Earth", DateTimeOffset.UtcNow));

        Assert.Contains("Middle/Earth", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Builds a clock stopped at a moment, reading dates in a given zone.</summary>
    /// <param name="timeZone">The schedule's time zone.</param>
    /// <param name="utcNow">The moment the clock reports.</param>
    /// <returns>The clock.</returns>
    private static ScheduleClock NewClock(string timeZone, DateTimeOffset utcNow) =>
        new(new FixedTimeProvider(utcNow), Options.Create(new BookingOptions { TimeZone = timeZone }));
}
