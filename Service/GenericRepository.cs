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

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        // Filtering in-memory (instead of Db.Table<T>().Where(predicate)) sidesteps
        // any inconsistencies in SQLite-net's expression-to-SQL translation for
        // compound predicates. Table sizes in this app are small enough that the
        // extra materialization cost is negligible, and correctness matters more.
        var all = await Db.Table<T>().ToListAsync();
        var compiled = predicate.Compile();
        return all.Where(compiled).ToList();
    }

    public Task<int> AddAsync(T entity)
        => Db.InsertAsync(entity);

    public Task<int> UpdateAsync(T entity)
        => Db.UpdateAsync(entity);

    public Task<int> DeleteAsync(T entity)
        => Db.DeleteAsync(entity);

    public Task<int> CountAsync()
        => Db.Table<T>().CountAsync();
}
