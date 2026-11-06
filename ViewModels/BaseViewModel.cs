using CommunityToolkit.Mvvm.ComponentModel;

namespace MoneySpend.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>
    /// Wraps an async operation with busy-state + error handling so every
    /// command doesn't have to repeat the same try/catch/finally boilerplate.
    /// Returns true if the operation finished without an exception.
    /// Errors are shown in an alert (set showError: false to keep them silent).
    /// </summary>
    protected async Task<bool> ExecuteAsync(Func<Task> operation, bool showError = true)
    {
        if (IsBusy) return false;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await operation();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[ExecuteAsync] {ex}");

            if (showError && Shell.Current is not null)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        Shell.Current.DisplayAlert("Error", ex.Message, "OK"));
                }
                catch (Exception alertEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExecuteAsync] alert failed: {alertEx}");
                }
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}