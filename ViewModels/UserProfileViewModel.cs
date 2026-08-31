using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class UserProfileViewModel : BaseViewModel
{
    private readonly IUserProfileService _userProfileService;

    private UserProfile? _existingProfile;

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string fullName = string.Empty;

    [ObservableProperty]
    private string mobileNo = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string currencyCode = "INR";

    [ObservableProperty]
    private string? successMessage;

    public List<string> CurrencyCodes { get; } = new() { "INR", "USD", "EUR", "GBP", "AED" };

    public UserProfileViewModel(IUserProfileService userProfileService)
    {
        _userProfileService = userProfileService;
        Title = "My Profile";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            _existingProfile = await _userProfileService.GetProfileAsync();
            if (_existingProfile is null)
            {
                IsEditMode = false;
                return;
            }

            IsEditMode = true;
            FullName = _existingProfile.FullName;
            MobileNo = _existingProfile.MobileNo;
            Email = _existingProfile.Email;
            CurrencyCode = _existingProfile.CurrencyCode;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SuccessMessage = null;

        if (string.IsNullOrWhiteSpace(FullName))
        {
            ErrorMessage = "Enter your name.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingProfile is not null)
            {
                _existingProfile.FullName = FullName;
                _existingProfile.MobileNo = MobileNo;
                _existingProfile.Email = Email;
                _existingProfile.CurrencyCode = CurrencyCode;

                await _userProfileService.UpdateAsync(_existingProfile);
            }
            else
            {
                var profile = new UserProfile
                {
                    FullName = FullName,
                    MobileNo = MobileNo,
                    Email = Email,
                    CurrencyCode = CurrencyCode
                };
                await _userProfileService.CreateAsync(profile);
                _existingProfile = profile;
                IsEditMode = true;
            }

            SuccessMessage = "Profile saved!";
        });
    }
}
