using Arthiva.Models;

namespace Arthiva.Services;

public class CategoryService : ICategoryService
{
    private readonly IGenericRepository<Category> _repo;

    public CategoryService(IGenericRepository<Category> repo)
    {
        _repo = repo;
    }

    public async Task<List<Category>> GetAllAsync()
    {
        var categories = await _repo.FindAsync(c => !c.IsDeleted && c.IsActive);
        return categories.OrderBy(c => c.DisplayOrder).ToList();
    }

    public async Task<List<Category>> GetByTypeAsync(string type)
    {
        var categories = await _repo.FindAsync(c => !c.IsDeleted && c.IsActive && c.Type == type);
        return categories.OrderBy(c => c.DisplayOrder).ToList();
    }

    public async Task<List<Category>> GetFavoritesAsync()
    {
        var categories = await _repo.FindAsync(c => !c.IsDeleted && c.IsActive && c.IsFavorite);
        return categories.OrderBy(c => c.DisplayOrder).ToList();
    }

    public async Task<List<Category>> GetSubCategoriesAsync(int parentId)
    {
        var categories = await _repo.FindAsync(c => !c.IsDeleted && c.IsActive && c.ParentId == parentId);
        return categories.OrderBy(c => c.DisplayOrder).ToList();
    }

    public Task<Category?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public Task<int> CreateAsync(Category category)
    {
        category.CreatedAt = DateTime.UtcNow;
        category.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(category);
    }

    public Task<int> UpdateAsync(Category category)
    {
        category.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(category);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var category = await _repo.GetByIdAsync(id);
        if (category is null) return 0;

        category.IsDeleted = true;
        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        return await _repo.UpdateAsync(category);
    }

    public async Task ToggleFavoriteAsync(int id)
    {
        var category = await _repo.GetByIdAsync(id);
        if (category is null) return;

        category.IsFavorite = !category.IsFavorite;
        category.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(category);
    }
}
