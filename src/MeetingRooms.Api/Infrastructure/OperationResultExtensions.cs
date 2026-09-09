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
    /// <exception cref="InvalidOperationException">Thrown if the result actually succeeded.</exception>
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
            _ => throw new InvalidOperationException("A successful result has no error representation."),
        };

        return controller.Problem(detail: result.Detail, statusCode: status, title: title);
    }
}
