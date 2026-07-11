using CustomerService.Domain.Entities;

namespace CustomerService.Domain.Interfaces;

/// <summary>
/// Generic read/write repository contract for entity <typeparamref name="T"/>.
/// Keeps the Application layer decoupled from EF Core / infrastructure.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>Returns a queryable, untracked set for read operations.</summary>
    /// <returns>An <see cref="IQueryable{T}"/> over the entity set.</returns>
    IQueryable<T> Query();

    /// <summary>Returns an entity by primary key, or null if not found.</summary>
    /// <param name="id">Primary key value.</param>
    /// <returns>The entity or null.</returns>
    Task<T?> GetByIdAsync(object id);

    /// <summary>Adds a new entity.</summary>
    /// <param name="entity">Entity to add.</param>
    Task AddAsync(T entity);

    /// <summary>Updates an existing entity.</summary>
    /// <param name="entity">Entity to update.</param>
    void Update(T entity);

    /// <summary>Removes an entity.</summary>
    /// <param name="entity">Entity to remove.</param>
    void Remove(T entity);

    /// <summary>Persists all pending changes.</summary>
    /// <returns>The number of affected rows.</returns>
    Task<int> SaveChangesAsync();
}
