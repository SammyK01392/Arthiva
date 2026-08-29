using System.Linq.Expressions;
using Arthiva.Data;
using SQLite;

namespace Arthiva.Services;

public class GenericRepository<T> : IGenericRepository<T> where T : new()
{
    protected readonly SQLiteAsyncConnection Db;

    public GenericRepository(ArthivaDatabase database)
    {
        Db = database.Database;
    }

    public Task<List<T>> GetAllAsync()
        => Db.Table<T>().ToListAsync();

    public Task<T?> GetByIdAsync(int id)
        => Db.FindAsync<T>(id);

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate)
        => Db.Table<T>().Where(predicate).ToListAsync();

    public Task<int> AddAsync(T entity)
        => Db.InsertAsync(entity);

    public Task<int> UpdateAsync(T entity)
        => Db.UpdateAsync(entity);

    public Task<int> DeleteAsync(T entity)
        => Db.DeleteAsync(entity);

    public Task<int> CountAsync()
        => Db.Table<T>().CountAsync();
}
