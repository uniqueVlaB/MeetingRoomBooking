namespace MeetingRooms.Tests.Infrastructure;

/// <summary>A clock stopped at a chosen moment.</summary>
/// <remarks>
/// Used where a test needs to produce something the real clock would have to wait for — an already
/// expired access token, for instance — rather than sleeping or weakening the production rule.
/// </remarks>
/// <param name="utcNow">The moment this clock reports.</param>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private readonly DateTimeOffset utcNow = utcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => this.utcNow;
}
