using MeetingRooms.Core.Abstractions;
using MeetingRooms.Infrastructure.SQL.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.SQL.Concurrency;

/// <summary>
/// Recognises the SQL Server unique-index violation raised when a booking loses the race for a slot.
/// </summary>
/// <remarks>
/// This is the only place in the solution that knows SQL Server error numbers. Keeping it here,
/// behind <see cref="ISlotConflictDetector"/>, is what lets the domain express "somebody else got
/// the slot" without depending on a database provider. See <c>docs/concurrency.md</c>.
/// </remarks>
public sealed class SqlServerSlotConflictDetector : ISlotConflictDetector
{
    /// <summary>Cannot insert duplicate key row in an object with a unique index.</summary>
    private const int DuplicateKeyRow = 2601;

    /// <summary>Violation of a unique constraint or primary key.</summary>
    private const int UniqueConstraintViolation = 2627;

    /// <inheritdoc />
    public bool IsActiveSlotConflict(Exception exception) =>
        IsUniqueViolation(exception, AppDbContext.ActiveSlotIndexName);

    /// <summary>
    /// Determines whether an exception is a unique-index violation, optionally on a named index.
    /// </summary>
    /// <param name="exception">The exception thrown by a save.</param>
    /// <param name="indexName">
    /// When supplied, the violation must also name this index. Matching on the name is what stops
    /// an unrelated duplicate — a repeated room name, for instance — from being reported to the user
    /// as "this slot is already booked".
    /// </param>
    /// <returns><see langword="true"/> if the exception represents that unique violation.</returns>
    public static bool IsUniqueViolation(Exception exception, string? indexName = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // EF wraps provider exceptions in DbUpdateException; the SqlException may also arrive bare
        // when the write did not go through the change tracker.
        var sqlException = exception as SqlException
            ?? (exception as DbUpdateException)?.InnerException as SqlException;

        if (sqlException is null)
        {
            return false;
        }

        foreach (SqlError error in sqlException.Errors)
        {
            if (error.Number is not (DuplicateKeyRow or UniqueConstraintViolation))
            {
                continue;
            }

            if (indexName is null || error.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
