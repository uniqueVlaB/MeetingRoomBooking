using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Database;

namespace MeetingRooms.Infrastructure.SQL.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>, scoped to one request.
/// </summary>
/// <remarks>
/// <see cref="SaveChangesAsync"/> intentionally lets provider exceptions through. The booking flow
/// depends on seeing the unique-index violation in order to translate it into a conflict response;
/// catching it here would turn a clean 409 into either a 500 or, worse, a silent success.
/// </remarks>
/// <param name="dbContext">The request's context.</param>
internal sealed class UnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    private readonly AppDbContext dbContext = dbContext;

    /// <inheritdoc />
    public IRepository<Room> Rooms { get; } = new Repository<Room>(dbContext);

    /// <inheritdoc />
    public IRepository<TimeSlot> TimeSlots { get; } = new Repository<TimeSlot>(dbContext);

    /// <inheritdoc />
    public IRepository<Booking> Bookings { get; } = new Repository<Booking>(dbContext);

    /// <inheritdoc />
    public IRepository<RefreshToken> RefreshTokens { get; } = new Repository<RefreshToken>(dbContext);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        this.dbContext.SaveChangesAsync(cancellationToken);
}
