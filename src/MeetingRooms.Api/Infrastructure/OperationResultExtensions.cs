using MeetingRooms.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Infrastructure;

/// <summary>
/// Maps a failed <see cref="OperationResult{TValue}"/> onto an HTTP response.
/// </summary>
/// <remarks>
/// Centralising the mapping is what keeps the concurrency contract honest: a lost race always leaves
/// through the same door, as <c>409 Conflict</c> with an RFC 9457 problem-details body, and cannot
/// drift into a 500 in one controller and a 200 in another. If a new outcome is added, the switch
/// below is the one place that must learn about it.
/// </remarks>
public static class OperationResultExtensions
{
    /// <summary>
    /// Converts a failed result into the matching error response.
    /// </summary>
    /// <typeparam name="TValue">The value type the result would have carried.</typeparam>
    /// <param name="result">The failed result.</param>
    /// <param name="controller">The controller producing the response.</param>
    /// <returns>A problem-details result carrying the appropriate status code.</returns>
    public static ActionResult ToErrorResult<TValue>(
        this OperationResult<TValue> result,
        ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        var (status, title) = result.Outcome switch
        {
            OperationOutcome.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            OperationOutcome.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            OperationOutcome.Forbidden => (StatusCodes.Status403Forbidden, "Not allowed"),
            OperationOutcome.Invalid => (StatusCodes.Status400BadRequest, "Invalid request"),

            // Callers reach here through "!IsSuccess || Value is null", so a success that somehow
            // carries no value arrives too. Throwing would turn it into an opaque 500; 500 is the
            // right code, but it should say what happened.
            _ => (StatusCodes.Status500InternalServerError, "Unexpected result"),
        };

        var detail = result.Detail
            ?? (result.IsSuccess
                ? "The operation reported success but produced no result."
                : null);

        return controller.Problem(detail: detail, statusCode: status, title: title);
    }
}
