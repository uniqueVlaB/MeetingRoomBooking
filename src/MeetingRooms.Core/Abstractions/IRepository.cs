namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// Read and write access to one entity type.
/// </summary>
/// <remarks>
/// Deliberately thin. It exposes <see cref="Query"/> so services can compose the projections they
/// need rather than forcing every query through a bespoke repository method, while keeping the
/// concrete <c>DbContext</c> out of Core.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>A tracked query over the entity set, for reads that precede a write.</summary>
    /// <returns>A composable query.</returns>
    IQueryable<TEntity> Query();

    /// <summary>An untracked query, for reads whose results are only projected and returned.</summary>
    /// <returns>A composable query that does not populate the change tracker.</returns>
    IQueryable<TEntity> QueryAsNoTracking();

    /// <summary>Finds an entity by primary key.</summary>
    /// <param name="id">The primary key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The entity, or <see langword="null"/> when no row has that key.</returns>
    ValueTask<TEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages an entity for insertion on the next save.</summary>
    /// <param name="entity">The entity to insert.</param>
    void Add(TEntity entity);

    /// <summary>Stages an entity for deletion on the next save.</summary>
    /// <param name="entity">The entity to delete.</param>
    void Remove(TEntity entity);
}
