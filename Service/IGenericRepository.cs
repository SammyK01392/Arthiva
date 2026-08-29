using System.Linq.Expressions;

namespace Arthiva.Services;

/// <summary>
/// Generic repository contract used by all entity-specific services.
/// Keeps raw SQLite access out of the service/business-logic layer.
/// </summary>
public interface IGenericRepository<T> where T : new()
{
    Task<List<T>> GetAllAsync();

    Task<T?> GetByIdAsync(int id);

    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);

    Task<int> AddAsync(T entity);

    Task<int> UpdateAsync(T entity);

    /// <summary>
    /// Hard delete. Most services should prefer soft-delete (IsDeleted flag)
    /// via their own SoftDeleteAsync method instead of calling this directly.
    /// </summary>
    Task<int> DeleteAsync(T entity);

    Task<int> CountAsync();
}
