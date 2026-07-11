using CustomerService.Domain.Interfaces;
using CustomerService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Infrastructure.Repositories;

/// <summary>
/// Generic EF Core implementation of <see cref="IRepository{T}"/>.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public class Repository<T> : IRepository<T> where T : class
{
    private readonly AppDbContext _context;
    private readonly DbSet<T> _set;

    /// <summary>Initializes a new <see cref="Repository{T}"/>.</summary>
    /// <param name="context">The database context.</param>
    public Repository(AppDbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    /// <inheritdoc/>
    public IQueryable<T> Query() => _set.AsNoTracking();

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(object id) => await _set.FindAsync(id);

    /// <inheritdoc/>
    public async Task AddAsync(T entity) => await _set.AddAsync(entity);

    /// <inheritdoc/>
    public void Update(T entity) => _set.Update(entity);

    /// <inheritdoc/>
    public void Remove(T entity) => _set.Remove(entity);

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
}
