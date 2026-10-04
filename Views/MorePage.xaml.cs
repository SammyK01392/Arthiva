using MoneySpend.Services;

namespace MoneySpend.Views;

public partial class MorePage : ContentPage
{
    private readonly IUserProfileService _userProfileService;

    public MorePage(IUserProfileService userProfileService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var profile = await _userProfileService.GetProfileAsync();

            if (profile is null)
            {
                ProfileNameLabel.Text = "My Profile";
                ProfileSubLabel.Text = "View and edit your details";
                ProfileImage.IsVisible = false;
                AvatarPlaceholder.IsVisible = true;
                return;
            }

            ProfileNameLabel.Text = string.IsNullOrWhiteSpace(profile.FullName)
                ? "My Profile"
                : profile.FullName;

            ProfileSubLabel.Text =
                !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email :
                !string.IsNullOrWhiteSpace(profile.MobileNo) ? profile.MobileNo :
                "View and edit your details";

            var hasImage = !string.IsNullOrEmpty(profile.ProfileImage)
                           && File.Exists(profile.ProfileImage);

            ProfileImage.Source = hasImage ? ImageSource.FromFile(profile.ProfileImage) : null;
            ProfileImage.IsVisible = hasImage;
            AvatarPlaceholder.IsVisible = !hasImage;
        }
        catch
        {
            // Profile load fail ho to default hero hi dikhne do
        }
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(UserProfilePage));

    private async void OnBillsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BillListPage));

    private async void OnEmiTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(EmiListPage));

    private async void OnBorrowLendTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BorrowLendListPage));

    private async void OnContactsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(ContactListPage));

    private async void OnSavingGoalsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(SavingGoalListPage));

    private async void OnBudgetTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BudgetListPage));

    private async void OnCategoriesTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(CategoryListPage));

    private async void OnComingSoonTapped(object? sender, TappedEventArgs e)
        => await DisplayAlert("Coming Soon", "This feature is under development.", "OK");

    private async void OnShareCrashLogTapped(object? sender, TappedEventArgs e)
    {
        if (!CrashLogger.HasLog())
        {
            await DisplayAlert("No Crashes", "No crash log found yet.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "MoneySpend Crash Log",
            File = new ShareFile(CrashLogger.FilePath)
        });
    }

    private async void OnBackupRestoreTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BackupRestorePage));

    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

    private async void OnSecurityTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(SecurityPage));

    private async void OnAboutDeveloperTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(AboutDeveloperPage));
}