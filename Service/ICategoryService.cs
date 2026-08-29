using Arthiva.Models;

namespace Arthiva.Services;

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync();

    /// <summary>Type = "Income" or "Expense"</summary>
    Task<List<Category>> GetByTypeAsync(string type);

    Task<List<Category>> GetFavoritesAsync();

    Task<List<Category>> GetSubCategoriesAsync(int parentId);

    Task<Category?> GetByIdAsync(int id);

    Task<int> CreateAsync(Category category);

    Task<int> UpdateAsync(Category category);

    Task<int> SoftDeleteAsync(int id);

    Task ToggleFavoriteAsync(int id);
}
