using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace CustomerService.Tests.Fakes;

/// <summary>
/// In-memory implementation of <see cref="IRepository{T}"/> for unit tests.
/// Stores entities in a <see cref="List{T}"/> keyed by an integer "Id"
/// property discovered via reflection, so the Application services can be
/// exercised without a real database. <see cref="Query"/> returns an
/// async-capable <see cref="IQueryable{T}"/> so EF Core async operators
/// (CountAsync, FirstOrDefaultAsync, ToListAsync) work in tests.
/// </summary>
/// <typeparam name="T">Entity type (expected to expose an int Id).</typeparam>
public class FakeRepository<T> : IRepository<T> where T : class
{
    private readonly List<T> _items = new();
    private int _nextId = 1;

    /// <summary>All stored entities (untracked, async-capable).</summary>
    public IQueryable<T> Query() => new AsyncEnumerableAdapter<T>(_items.AsQueryable());

    /// <summary>Finds an entity by its Id (int or string primary key).</summary>
    public Task<T?> GetByIdAsync(object id)
    {
        var idProp = typeof(T).GetProperty("Id")!;
        var found = _items.FirstOrDefault(x =>
        {
            var value = idProp.GetValue(x)!;
            return id switch
            {
                int i => value is int vi && vi == i,
                string s => value is string vs && vs == s,
                _ => Equals(value, id),
            };
        });
        return Task.FromResult(found);
    }

    /// <summary>Adds an entity, assigning the next int Id unless one is already set.</summary>
    public Task AddAsync(T entity)
    {
        var idProp = typeof(T).GetProperty("Id");
        if (idProp is not null && idProp.PropertyType == typeof(int))
        {
            // Preserve an explicitly-set (non-zero) id so tests can control keys;
            // otherwise assign the next auto-incrementing id.
            if ((int)idProp.GetValue(entity)! == 0)
            {
                idProp.SetValue(entity, _nextId++);
            }
        }
        _items.Add(entity);
        return Task.CompletedTask;
    }

    /// <summary>No-op for the in-memory store (items are referenced).</summary>
    public void Update(T entity) { }

    /// <summary>Removes an entity by Id.</summary>
    public void Remove(T entity)
    {
        var idProp = typeof(T).GetProperty("Id")!;
        var id = (int)idProp.GetValue(entity)!;
        var found = _items.FirstOrDefault(x => (int)idProp.GetValue(x)! == id);
        if (found is not null) _items.Remove(found);
    }

    /// <summary>Persists changes (no-op for the in-memory store).</summary>
    public Task<int> SaveChangesAsync() => Task.FromResult(0);
}

/// <summary>
/// Minimal async-enumerable wrapper that lets EF Core's async extension
/// methods execute against an in-memory <see cref="List{T}"/>. Only the
/// members used by the Application services are implemented.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
internal class AsyncEnumerableAdapter<T> : IQueryable<T>, IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    private readonly IQueryable<T> _source;

    public AsyncEnumerableAdapter(IEnumerable<T> source) => _source = source.AsQueryable();

    public Type ElementType => _source.ElementType;
    public Expression Expression => _source.Expression;
    public IQueryProvider Provider => new AsyncQueryProvider(_source.Provider);

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new AsyncEnumerator(_source.GetEnumerator());

    public IEnumerator<T> GetEnumerator() => _source.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private class AsyncQueryProvider : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;
        public AsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => _inner.CreateQuery(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            => new AsyncEnumerableAdapter<TElement>(_inner.CreateQuery<TElement>(expression));
        public object? Execute(Expression expression) => _inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            // EF's async operators (CountAsync, FirstOrDefaultAsync, etc.)
            // expect a Task<T> / ValueTask<T> wrapping the result. The inner
            // LINQ provider returns the value synchronously, so we wrap it.
            var value = _inner.Execute(expression);
            var resultType = typeof(TResult);
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var inner = resultType.GetGenericArguments()[0];
                var vt = typeof(ValueTask<>).MakeGenericType(inner)
                    .GetConstructor(new[] { inner })!.Invoke(new[] { value })!;
                return (TResult)vt;
            }
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var taskInner = resultType.GetGenericArguments()[0];
                var fromResult = typeof(Task).GetMethod("FromResult")!
                    .MakeGenericMethod(taskInner);
                var task = fromResult.Invoke(null, new[] { value })!;
                return (TResult)task;
            }
            // Fallback: return the value directly (should not happen for EF async).
            return (TResult)value!;
        }
    }

    private class AsyncEnumerator : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public AsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
        public T Current => _inner.Current;
        public ValueTask<bool> MoveNextAsync() => new(_inner.MoveNext());
        public ValueTask DisposeAsync() { _inner.Dispose(); return default; }
    }
}
