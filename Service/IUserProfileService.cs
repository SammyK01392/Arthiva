using MoneySpend.Models;

namespace MoneySpend.Services;

public interface IUserProfileService
{
    /// <summary>MoneySpend is single-profile local app — returns the one active profile, if any.</summary>
    Task<UserProfile?> GetProfileAsync();

    Task<int> CreateAsync(UserProfile profile);

    Task<int> UpdateAsync(UserProfile profile);

    Task<int> UpdateCurrencyAsync(string currencyCode);

    Task<bool> ProfileExistsAsync();

    Task ResetProfileAsync();
}
