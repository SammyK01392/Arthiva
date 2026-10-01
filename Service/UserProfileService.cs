using Arthiva.Models;

namespace Arthiva.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IGenericRepository<UserProfile> _repo;

    public UserProfileService(IGenericRepository<UserProfile> repo)
    {
        _repo = repo;
    }

    public async Task<UserProfile?> GetProfileAsync()
    {
        var all = await _repo.GetAllAsync();
        return all.FirstOrDefault(p => p.IsActive);
    }

    public Task<int> CreateAsync(UserProfile profile)
    {
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(profile);
    }

    public Task<int> UpdateAsync(UserProfile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(profile);
    }

    public async Task<int> UpdateCurrencyAsync(string currencyCode)
    {
        var profile = await GetProfileAsync();
        if (profile is null) return 0;

        profile.CurrencyCode = currencyCode;
        profile.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(profile);
    }

    public async Task<bool> ProfileExistsAsync()
        => await _repo.CountAsync() > 0;

    public async Task ResetProfileAsync()
    {
        var profile = await GetProfileAsync();
        if (profile is not null)
            await _repo.DeleteAsync(profile);

        // NOTE: Arthiva is single-profile, but this only deletes UserProfile.
        // Agar transactions/budgets waghera dusri tables mein hain, unhe bhi
        // yahin delete karna padega taaki koi orphan financial data na bache.
        // e.g. await _transactionRepo.DeleteAllAsync(); etc.
    }
}
