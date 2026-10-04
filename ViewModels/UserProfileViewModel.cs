using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class UserProfileViewModel : BaseViewModel
{
    private readonly IUserProfileService _userProfileService;

    private UserProfile? _existingProfile;
    private string? _originalImagePath; // DB mein jo image saved hai

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileImage))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ProfileImageSource))]
    private string? profileImagePath;

    public bool HasProfileImage =>
        !string.IsNullOrEmpty(ProfileImagePath) && File.Exists(ProfileImagePath);

    public bool ShowPlaceholder => !HasProfileImage;

    public ImageSource? ProfileImageSource =>
        HasProfileImage ? ImageSource.FromFile(ProfileImagePath) : null;

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

            _originalImagePath = _existingProfile.ProfileImage;
            ProfileImagePath = _existingProfile.ProfileImage;
        });
    }

    [RelayCommand]
    private async Task PickFromGalleryAsync()
    {
        await ExecuteAsync(async () =>
        {
            var file = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select profile photo"
            });
            await SetNewImageAsync(file);
        });
    }

    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ErrorMessage = "Camera is device par supported nahi hai.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var file = await MediaPicker.Default.CapturePhotoAsync();
            await SetNewImageAsync(file);
        });
    }

    [RelayCommand]
    private void RemoveImage()
    {
        DeleteUnsavedImage();
        ProfileImagePath = null;
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
                _existingProfile.ProfileImage = ProfileImagePath;

                await _userProfileService.UpdateAsync(_existingProfile);
            }
            else
            {
                var profile = new UserProfile
                {
                    FullName = FullName,
                    MobileNo = MobileNo,
                    Email = Email,
                    CurrencyCode = CurrencyCode,
                    ProfileImage = ProfileImagePath
                };
                await _userProfileService.CreateAsync(profile);
                _existingProfile = profile;
                IsEditMode = true;
            }

            // Save ke baad purani image file safely delete
            if (!string.IsNullOrEmpty(_originalImagePath)
                && _originalImagePath != ProfileImagePath
                && File.Exists(_originalImagePath))
            {
                File.Delete(_originalImagePath);
            }
            _originalImagePath = ProfileImagePath;

            SuccessMessage = "Profile saved!";
        });
    }

    private async Task SetNewImageAsync(FileResult? file)
    {
        if (file is null) return; // user ne cancel kiya

        var newPath = await SaveToAppDataAsync(file);

        DeleteUnsavedImage();       // pehle wali unsaved pick delete
        ProfileImagePath = newPath; // naya set (replace)
    }

    // Sirf wahi file delete karo jo abhi tak DB mein save nahi hui
    private void DeleteUnsavedImage()
    {
        if (!string.IsNullOrEmpty(ProfileImagePath)
            && ProfileImagePath != _originalImagePath
            && File.Exists(ProfileImagePath))
        {
            File.Delete(ProfileImagePath);
        }
    }

    private static async Task<string> SaveToAppDataAsync(FileResult file)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var dir = Path.Combine(FileSystem.AppDataDirectory, "profile");
        Directory.CreateDirectory(dir);

        // Unique naam, taaki replace karne par purani image cache na dikhe
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");

        await using var source = await file.OpenReadAsync();
        await using var dest = File.Create(path);
        await source.CopyToAsync(dest);

        return path;
    }
}