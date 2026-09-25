namespace Arthiva.Views;

/// <summary>
/// First screen shown when no UserProfile exists yet. Collects Full Name
/// and Mobile Number, then hands off to CreatePinPage. This page is the
/// root of its NavigationPage (see App.xaml.cs) — pressing Android Back
/// here exits the app rather than revealing AppShell/Dashboard, since
/// AppShell isn't created until PIN setup succeeds.
/// </summary>
public partial class FirstTimeSetupPage : ContentPage
{
    private readonly IServiceProvider _serviceProvider;
    private bool _isSubmitting;

    public FirstTimeSetupPage(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        NavigationPage.SetHasNavigationBar(this, false);
    }

    private void OnFullNameChanged(object? sender, TextChangedEventArgs e)
        => FullNameErrorLabel.IsVisible = false;

    private void OnMobileNoChanged(object? sender, TextChangedEventArgs e)
    {
        MobileNoErrorLabel.IsVisible = false;

        // Belt-and-braces: keep only digits even though the numeric
        // keyboard already restricts input on most devices/IMEs.
        var digitsOnly = new string(Array.FindAll((e.NewTextValue ?? string.Empty).ToCharArray(), char.IsDigit));
        if (digitsOnly.Length > 10)
            digitsOnly = digitsOnly[..10];

        if (digitsOnly != e.NewTextValue)
            MobileNoEntry.Text = digitsOnly;
    }

    private async void OnContinueTapped(object? sender, TappedEventArgs e)
    {
        if (_isSubmitting)
            return;

        var fullName = (FullNameEntry.Text ?? string.Empty).Trim();
        var mobileNo = (MobileNoEntry.Text ?? string.Empty).Trim();

        var isValid = true;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            FullNameErrorLabel.Text = "Full name is required.";
            FullNameErrorLabel.IsVisible = true;
            isValid = false;
        }

        if (mobileNo.Length != 10 || !IsAllDigits(mobileNo))
        {
            MobileNoErrorLabel.Text = "Enter a valid 10-digit mobile number.";
            MobileNoErrorLabel.IsVisible = true;
            isValid = false;
        }

        if (!isValid)
            return;

        try
        {
            _isSubmitting = true;
            SetBusy(true);

            var createPinPage = _serviceProvider.GetRequiredService<CreatePinPage>();
            createPinPage.Initialize(fullName, mobileNo);

            await Navigation.PushAsync(createPinPage);
        }
        finally
        {
            _isSubmitting = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ContinueButtonBorder.Opacity = busy ? 0.7 : 1.0;
        ContinueButtonBorder.IsEnabled = !busy;
        ContinueBusyIndicator.IsVisible = busy;
        ContinueBusyIndicator.IsRunning = busy;
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return true;
    }
}
