using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Validation;
using Xunit;

namespace MeetingRooms.Tests.Domain;

/// <summary>
/// The slot rules, which are the one part of the no-double-booking guarantee the database cannot
/// enforce.
/// </summary>
/// <remarks>
/// The unique index stops two bookings of the same slot row. It cannot stop two <em>different</em>
/// slot rows from covering the same hour, because those rows are genuinely distinct — so a room
/// defined with 09:00-11:00 and 10:00-12:00 would let the same physical hour be sold twice with the
/// index none the wiser. That is why overlap is rejected at the point slots are defined, and why
/// these boundaries are worth pinning precisely.
/// </remarks>
public sealed class CreateRoomRequestValidatorTests
{
    private readonly CreateRoomRequestValidator validator = new();

    /// <summary>Slots that touch but do not overlap are the normal case and must be allowed.</summary>
    /// <remarks>
    /// The boundary between a correct comparison and an off-by-one: an hourly template is entirely
    /// made of slots that start exactly when the previous one ends.
    /// </remarks>
    [Fact]
    public void AdjacentSlots_AreAllowed()
    {
        var result = this.validator.Validate(Room(
            (new TimeOnly(9, 0), new TimeOnly(10, 0)),
            (new TimeOnly(10, 0), new TimeOnly(11, 0)),
            (new TimeOnly(11, 0), new TimeOnly(12, 0))));

        Assert.True(result.IsValid, string.Join(" ", result.Errors.Select(error => error.ErrorMessage)));
    }

    /// <summary>A single slot has nothing to overlap with.</summary>
    [Fact]
    public void ASingleSlot_IsAllowed()
    {
        var result = this.validator.Validate(Room((new TimeOnly(9, 0), new TimeOnly(17, 0))));

        Assert.True(result.IsValid);
    }

    /// <summary>Partly overlapping slots are rejected.</summary>
    [Fact]
    public void PartlyOverlappingSlots_AreRejected()
    {
        var result = this.validator.Validate(Room(
            (new TimeOnly(9, 0), new TimeOnly(11, 0)),
            (new TimeOnly(10, 0), new TimeOnly(12, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// A slot wholly inside another is rejected.
    /// </summary>
    /// <remarks>
    /// The case a naive "does this start before the previous one ended" check gets right only
    /// because the list is sorted first. Worth its own test, because containment is the shape most
    /// likely to slip through a rewrite.
    /// </remarks>
    [Fact]
    public void ASlotContainedWithinAnother_IsRejected()
    {
        var result = this.validator.Validate(Room(
            (new TimeOnly(9, 0), new TimeOnly(12, 0)),
            (new TimeOnly(10, 0), new TimeOnly(11, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>Two identical slots are rejected.</summary>
    [Fact]
    public void DuplicateSlots_AreRejected()
    {
        var result = this.validator.Validate(Room(
            (new TimeOnly(9, 0), new TimeOnly(10, 0)),
            (new TimeOnly(9, 0), new TimeOnly(10, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>Overlap is detected regardless of the order the slots arrive in.</summary>
    /// <remarks>
    /// The validator sorts before comparing. Submitting the later slot first is the obvious way to
    /// defeat a comparison that trusted the incoming order.
    /// </remarks>
    [Fact]
    public void OverlapIsDetected_EvenWhenSlotsArriveOutOfOrder()
    {
        var result = this.validator.Validate(Room(
            (new TimeOnly(10, 0), new TimeOnly(12, 0)),
            (new TimeOnly(9, 0), new TimeOnly(11, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>A slot that ends when it starts covers no time at all.</summary>
    [Fact]
    public void AZeroLengthSlot_IsRejected()
    {
        var result = this.validator.Validate(Room((new TimeOnly(9, 0), new TimeOnly(9, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>A slot that ends before it starts is rejected.</summary>
    [Fact]
    public void ABackwardsSlot_IsRejected()
    {
        var result = this.validator.Validate(Room((new TimeOnly(11, 0), new TimeOnly(9, 0))));

        Assert.False(result.IsValid);
    }

    /// <summary>Builds a request carrying the given slots.</summary>
    /// <param name="slots">Start and end times.</param>
    /// <returns>The request.</returns>
    private static CreateRoomRequest Room(params (TimeOnly Start, TimeOnly End)[] slots) => new(
        "Test room",
        "Test wing",
        6,
        [.. slots.Select(slot => new TimeSlotRequest(slot.Start, slot.End))]);
}
