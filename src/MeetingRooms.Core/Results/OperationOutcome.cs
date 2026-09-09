namespace MeetingRooms.Core.Results;

/// <summary>
/// What happened when a domain operation was attempted.
/// </summary>
/// <remarks>
/// Services return one of these rather than throwing, so that losing a booking race is ordinary
/// control flow instead of an exception surfacing as HTTP 500. The API maps each member to a status
/// code in exactly one place, <c>OperationResultExtensions</c>, which is what stops a lost race
/// from becoming a 409 in one controller and a 500 in another.
/// </remarks>
public enum OperationOutcome
{
    /// <summary>The operation completed as requested.</summary>
    Success = 0,

    /// <summary>
    /// The request collided with existing state — most importantly, another request already holds
    /// the slot. This is the expected result for every loser of a concurrent booking race, and maps
    /// to HTTP 409.
    /// </summary>
    Conflict = 1,

    /// <summary>The room, slot or booking referenced by the request does not exist.</summary>
    NotFound = 2,

    /// <summary>The caller may not act on this resource — for example, it belongs to somebody else.</summary>
    Forbidden = 3,

    /// <summary>The request was well-formed but not allowed, for example a date in the past.</summary>
    Invalid = 4,
}
