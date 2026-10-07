using System.Linq.Expressions;
using MoneySpend.Data;
using SQLite;

namespace MoneySpend.Services;

public class GenericRepository<T> : IGenericRepository<T> where T : new()
{
    protected readonly SQLiteAsyncConnection Db;

    public GenericRepository(MoneySpendDatabase database)
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

    public async Task<int> AddAsync(T entity)
    {
        var result = await Db.InsertAsync(entity);
        DataChangeNotifier.Publish<T>();
        return result;
    }

    public async Task<int> UpdateAsync(T entity)
    {
        var result = await Db.UpdateAsync(entity);
        DataChangeNotifier.Publish<T>();
        return result;
    }

    public async Task<int> DeleteAsync(T entity)
    {
        var result = await Db.DeleteAsync(entity);
        DataChangeNotifier.Publish<T>();
        return result;
    }

    public Task<int> CountAsync()
        => Db.Table<T>().CountAsync();



}
