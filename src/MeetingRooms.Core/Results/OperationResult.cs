namespace MeetingRooms.Core.Results;

/// <summary>
/// The result of a domain operation: an <see cref="OperationOutcome"/> and, on success, a value.
/// </summary>
/// <typeparam name="TValue">Type produced when the operation succeeds.</typeparam>
/// <param name="Outcome">What happened.</param>
/// <param name="Value">The produced value, when the operation succeeded.</param>
/// <param name="Detail">Human-readable explanation, surfaced in the problem-details response.</param>
public sealed record OperationResult<TValue>(
    OperationOutcome Outcome,
    TValue? Value = default,
    string? Detail = null)
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => this.Outcome == OperationOutcome.Success;

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The produced value.</param>
    /// <returns>A success result carrying <paramref name="value"/>.</returns>
    public static OperationResult<TValue> Success(TValue value) =>
        new(OperationOutcome.Success, value);

    /// <summary>Creates a conflict result — for a booking, the slot is already taken.</summary>
    /// <param name="detail">Explanation for the caller.</param>
    /// <returns>A conflict result.</returns>
    public static OperationResult<TValue> Conflict(string detail) =>
        new(OperationOutcome.Conflict, Detail: detail);

    /// <summary>Creates a not-found result.</summary>
    /// <param name="detail">Explanation for the caller.</param>
    /// <returns>A not-found result.</returns>
    public static OperationResult<TValue> NotFound(string detail) =>
        new(OperationOutcome.NotFound, Detail: detail);

    /// <summary>Creates a forbidden result.</summary>
    /// <param name="detail">Explanation for the caller.</param>
    /// <returns>A forbidden result.</returns>
    public static OperationResult<TValue> Forbidden(string detail) =>
        new(OperationOutcome.Forbidden, Detail: detail);

    /// <summary>Creates an invalid-request result.</summary>
    /// <param name="detail">Explanation for the caller.</param>
    /// <returns>An invalid-request result.</returns>
    public static OperationResult<TValue> Invalid(string detail) =>
        new(OperationOutcome.Invalid, Detail: detail);
}
