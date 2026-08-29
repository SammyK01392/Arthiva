using Arthiva.Models;

namespace Arthiva.Services;

public interface IUserProfileService
{
    /// <summary>Arthiva is single-profile local app — returns the one active profile, if any.</summary>
    Task<UserProfile?> GetProfileAsync();

    Task<int> CreateAsync(UserProfile profile);

    Task<int> UpdateAsync(UserProfile profile);

    Task<int> UpdateCurrencyAsync(string currencyCode);

    Task<bool> ProfileExistsAsync();
}
