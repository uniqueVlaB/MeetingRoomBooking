using FluentValidation;
using MeetingRooms.Contracts.Rooms;

namespace MeetingRooms.Core.Validation;

/// <summary>
/// Validates the relationships between a new room's slots.
/// </summary>
/// <remarks>
/// Simple shape rules — required, length, range — are data annotations on the contract itself, which
/// ASP.NET Core enforces before a request ever reaches a controller. FluentValidation is used here
/// only for the rules that span several fields, which annotations cannot express: a slot must end
/// after it starts, and a room's slots must not overlap each other. Splitting the two this way
/// avoids maintaining the same rule in two places.
/// </remarks>
public sealed class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    /// <summary>Sets up the rules.</summary>
    public CreateRoomRequestValidator()
    {
        this.RuleForEach(request => request.TimeSlots)
            .Must(slot => slot.EndTime > slot.StartTime)
            .WithMessage("A slot must end after it starts.");

        this.RuleFor(request => request.TimeSlots)
            .Must(HaveNoOverlaps)
            .WithMessage("A room's slots must not overlap each other.");
    }

    /// <summary>
    /// Checks that no two slots share time.
    /// </summary>
    /// <remarks>
    /// Overlapping slots would let the same physical hour be booked twice through two different slot
    /// rows — the unique index cannot catch that, because the rows are genuinely distinct. The rule
    /// therefore belongs here, at the point where slots are defined.
    /// </remarks>
    /// <param name="slots">The proposed slots.</param>
    /// <returns><see langword="true"/> when the slots are disjoint.</returns>
    private static bool HaveNoOverlaps(IReadOnlyList<TimeSlotRequest> slots)
    {
        if (slots is null)
        {
            return true;
        }

        var ordered = slots.OrderBy(slot => slot.StartTime).ToList();

        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].StartTime < ordered[index - 1].EndTime)
            {
                return false;
            }
        }

        return true;
    }
}
