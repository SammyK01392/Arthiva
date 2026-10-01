using System.Text.RegularExpressions;
using Arthiva.Services;

namespace Arthiva.Views;

public partial class LoginPage : ContentPage
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$",
        RegexOptions.Compiled);

    private readonly IFirebaseAuthService _firebaseAuthService;

    private bool _isBusy;
    private bool _isPasswordVisible;

    public LoginPage(IFirebaseAuthService firebaseAuthService)
    {
        InitializeComponent();
        _firebaseAuthService = firebaseAuthService;
        NavigationPage.SetHasNavigationBar(this, false);
    }

    // ── Live email format feedback ──────────────────────────
    private void OnEmailChanged(object? sender, TextChangedEventArgs e)
    {
        HideError();

        var email = (e.NewTextValue ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(email))
        {
            EmailFormatErrorLabel.IsVisible = false;
            EmailBorder.Stroke = Color.FromArgb("#FFF1E3");
            return;
        }

        var isValid = EmailRegex.IsMatch(email);
        EmailFormatErrorLabel.IsVisible = !isValid;
        EmailBorder.Stroke = isValid ? Color.FromArgb("#FFF1E3") : Color.FromArgb("#FCA5A5");
    }

    // ── Show/Hide password ───────────────────────────────────
    private void OnTogglePasswordTapped(object? sender, TappedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        PasswordEntry.IsPassword = !_isPasswordVisible;
        TogglePasswordLabel.Text = _isPasswordVisible ? "Hide" : "Show";
    }

    // ── Login / Sign up ──────────────────────────────────────
    private async void OnLoginTapped(object? sender, TappedEventArgs e)
        => await SubmitAsync(isSignUp: false);

    private async void OnSignUpTapped(object? sender, TappedEventArgs e)
        => await SubmitAsync(isSignUp: true);

    private async Task SubmitAsync(bool isSignUp)
    {
        if (_isBusy)
            return;

        HideError();

        var email = (EmailEntry.Text ?? string.Empty).Trim();
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            ShowError("Enter a valid email address (e.g. name@example.com).");
            return;
        }

        if (password.Length < 6)
        {
            ShowError("Password must be at least 6 characters.");
            return;
        }

        try
        {
            _isBusy = true;
            SetBusy(isSignUp, true);

            if (isSignUp)
                await _firebaseAuthService.SignUpAsync(email, password);
            else
                await _firebaseAuthService.SignInAsync(email, password);

            // Login/signup done — go back to the caller (Backup & Restore page)
            // so the user can immediately act, rather than stranding them here.
            await Shell.Current.GoToAsync("..");
        }
        catch (HttpRequestException)
        {
            ShowError("Please check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _isBusy = false;
            SetBusy(isSignUp, false);
        }
    }

    // ── Forgot password ──────────────────────────────────────
    private async void OnForgotPasswordTapped(object? sender, TappedEventArgs e)
    {
        if (_isBusy)
            return;

        HideError();

        var email = (EmailEntry.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            ShowError("Enter your registered email in the field above first.");
            return;
        }

        var confirmed = await DisplayAlert(
            "Reset Password?",
            $"A password reset link will be sent to:\n{email}",
            "Send Link",
            "Cancel");

        if (!confirmed)
            return;

        try
        {
            _isBusy = true;
            SetBusy(isSignUp: false, busy: true);

            await _firebaseAuthService.SendPasswordResetAsync(email);

            await DisplayAlert(
                "Email Sent",
                "Check your inbox (and spam folder), then click the link to set a new password.",
                "OK");
        }
        catch (HttpRequestException)
        {
            ShowError("Please check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _isBusy = false;
            SetBusy(isSignUp: false, busy: false);
        }
    }

    // ── Helpers ──────────────────────────────────────────────
    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorBorder.IsVisible = true;
    }

    private void HideError()
    {
        ErrorBorder.IsVisible = false;
    }

    private void SetBusy(bool isSignUp, bool busy)
    {
        LoginButtonBorder.IsEnabled = !busy;
        SignUpButtonBorder.IsEnabled = !busy;
        LoginButtonBorder.Opacity = busy && !isSignUp ? 0.7 : 1.0;
        SignUpButtonBorder.Opacity = busy && isSignUp ? 0.7 : 1.0;

        if (isSignUp)
        {
            SignUpBusyIndicator.IsVisible = busy;
            SignUpBusyIndicator.IsRunning = busy;
        }
        else
        {
            LoginBusyIndicator.IsVisible = busy;
            LoginBusyIndicator.IsRunning = busy;
        }
    }
}