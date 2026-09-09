using MeetingRooms.Core.Abstractions;
using MeetingRooms.Infrastructure.SQL.Database;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.SQL.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// Internal on purpose: callers depend on the interface in Core, and nothing outside this assembly
/// should be able to reach for the context behind it.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="dbContext">The context this repository reads and writes through.</param>
internal sealed class Repository<TEntity>(AppDbContext dbContext) : IRepository<TEntity>
    where TEntity : class
{
    private readonly AppDbContext dbContext = dbContext;

    /// <inheritdoc />
    public IQueryable<TEntity> Query() => this.dbContext.Set<TEntity>();

    /// <inheritdoc />
    public IQueryable<TEntity> QueryAsNoTracking() => this.dbContext.Set<TEntity>().AsNoTracking();

    /// <inheritdoc />
    public ValueTask<TEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        this.dbContext.Set<TEntity>().FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public void Add(TEntity entity) => this.dbContext.Set<TEntity>().Add(entity);

    /// <inheritdoc />
    public void Remove(TEntity entity) => this.dbContext.Set<TEntity>().Remove(entity);
}
