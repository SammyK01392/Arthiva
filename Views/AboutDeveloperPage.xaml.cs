namespace MoneySpend.Views;

public partial class AboutDeveloperPage : ContentPage
{
    // ===== EDIT THESE =====
    private const string DevName = "Sameer Karale";
    private const string DevRole = "App Developer";
    private const string StudioName = "CodeCinema";
    private const string DevEmail = "codecinema365@gmail.com";
    // ======================

    public AboutDeveloperPage()
    {
        InitializeComponent();

        NameLabel.Text = DevName;
        RoleLabel.Text = DevRole;
        StudioLabel.Text = StudioName;
        EmailLabel.Text = DevEmail;
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString}";
        CopyrightLabel.Text = $"© {DateTime.Now.Year} {StudioName}. All rights reserved.";
    }

    private async void OnEmailTapped(object sender, TappedEventArgs e)
    {
        try
        {
            await Email.Default.ComposeAsync(new EmailMessage
            {
                Subject = "MoneySpend support",
                To = new List<string> { DevEmail }
            });
        }
        catch
        {
            await DisplayAlert("Email not available", $"Please write to us at {DevEmail}", "OK");
        }
    }

    private async void OnComingSoonTapped(object sender, TappedEventArgs e)
        => await DisplayAlert("Coming soon", "This feature will be available in a future update.", "OK");
}