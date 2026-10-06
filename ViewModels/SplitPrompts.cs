using System.Globalization;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>Small shared dialogs for the Split screens (amount prompt, account picker, alert).</summary>
public static class SplitPrompts
{
    public static Task AlertAsync(string message)
        => Shell.Current.DisplayAlert("Split", message, "OK");

    // ─────────────────────────────────────────────
    //  My own UPI id (stays on this phone only — Preferences)
    // ─────────────────────────────────────────────
    /// <summary>
    /// Full https URL of your hosted pay.html (see web/pay.html), e.g.
    /// "https://yourname.github.io/moneyspend-pay/pay.html".
    /// Leave empty to keep reminders exactly as before (no payment link).
    /// </summary>
    public const string PayPageUrl = "https://sammyk01392.github.io/Arthiva/pay.html";

    public const string MyUpiKey = "my_upi_id";
    public const string MyUpiAskedKey = "my_upi_asked";

    public static string GetMyUpi() => Preferences.Default.Get(MyUpiKey, string.Empty);

    /// <summary>Returns the saved id ("" if cleared), or null if cancelled/invalid.</summary>
    public static async Task<string?> AskMyUpiAsync()
    {
        var input = await Shell.Current.DisplayPromptAsync(
            "Your UPI ID",
            "Added to payment reminders so friends can pay you directly. Leave empty to remove.",
            accept: "Save",
            cancel: "Cancel",
            placeholder: "name@upi",
            initialValue: GetMyUpi());

        Preferences.Default.Set(MyUpiAskedKey, true);
        if (input is null) return null;

        input = input.Trim();
        if (input.Length == 0)
        {
            Preferences.Default.Remove(MyUpiKey);
            return string.Empty;
        }

        if (!SplitCalculator.IsValidUpi(input))
        {
            await AlertAsync("Enter a valid UPI ID like name@upi.");
            return null;
        }

        Preferences.Default.Set(MyUpiKey, input);
        return input;
    }

    // ─────────────────────────────────────────────
    //  Paying a friend: UPI app or "already paid"
    // ─────────────────────────────────────────────
    public static string BuildUpiLink(string upiId, string name, decimal amount, string note)
        => SplitCalculator.BuildUpiUri(upiId, name, amount, note) ?? string.Empty;

    /// <summary>
    /// Asks how the payment is made. True = go ahead and record it, false = cancelled.
    /// A UPI app can't report success back, so after opening it the user confirms manually.
    /// </summary>
    public static async Task<bool> ConfirmPaymentAsync(
        ISplitService splitService, int contactId, string friendName, decimal amount)
    {
        const string viaUpi = "Pay via UPI app";
        const string alreadyPaid = "Already paid (cash / other)";

        var choice = await Shell.Current.DisplayActionSheet(
            $"Pay ₹{amount:N2} to {friendName}", "Cancel", null, viaUpi, alreadyPaid);

        if (choice is null || choice == "Cancel") return false;
        if (choice != viaUpi) return true;

        var upi = await splitService.GetFriendUpiAsync(contactId);
        if (string.IsNullOrWhiteSpace(upi))
        {
            var entered = await Shell.Current.DisplayPromptAsync(
                $"{friendName}'s UPI ID",
                "Saved on their contact, so you won't be asked again.",
                accept: "Save",
                cancel: "Cancel",
                placeholder: "name@upi");

            if (string.IsNullOrWhiteSpace(entered)) return false;

            var saved = await splitService.SetFriendUpiAsync(contactId, entered);
            if (!saved.Success)
            {
                await AlertAsync(saved.ErrorMessage ?? "Could not save the UPI ID.");
                return false;
            }

            upi = entered.Trim();
        }

        var opened = false;
        try
        {
            opened = await Launcher.Default.OpenAsync(BuildUpiLink(upi!, friendName, amount, "MoneySpend split"));
        }
        catch
        {
            // no UPI app / unsupported platform → fall back below
        }

        if (!opened)
        {
            await Clipboard.Default.SetTextAsync(upi);
            await AlertAsync($"Couldn't open a UPI app here. {friendName}'s UPI ID ({upi}) is copied. Pay from your UPI app, then come back to confirm.");
        }

        return await Shell.Current.DisplayAlert(
            "Payment done?",
            $"Did the ₹{amount:N2} payment to {friendName} go through?",
            "Yes, record it", "No");
    }

    /// <summary>Returns null if cancelled or invalid (invalid shows an alert).</summary>
    public static async Task<decimal?> AskAmountAsync(string title, string message, decimal initial)
    {
        var input = await Shell.Current.DisplayPromptAsync(
            title,
            message,
            accept: "Next",
            cancel: "Cancel",
            keyboard: Keyboard.Numeric,
            initialValue: initial.ToString("0.##", CultureInfo.CurrentCulture));

        if (input is null) return null;

        var amount = SplitCalculator.ParseAmount(input);
        if (amount <= 0)
        {
            await AlertAsync("Enter a valid amount.");
            return null;
        }

        return amount;
    }

    /// <summary>
    /// Returns the chosen account id, or null if cancelled / no accounts.
    /// NOTE: assumes IAccountService.GetAllAsync() and Account.Name.
    /// </summary>
    public static async Task<int?> PickAccountAsync(IAccountService accountService, string question)
    {
        var accounts = (await accountService.GetAllAsync()).ToList();

        if (accounts.Count == 0)
        {
            await AlertAsync("Add an account first.");
            return null;
        }

        if (accounts.Count == 1) return accounts[0].Id;

        var names = accounts.Select(a => a.Name).ToArray();
        var picked = await Shell.Current.DisplayActionSheet(question, "Cancel", null, names);

        var index = picked is null ? -1 : Array.IndexOf(names, picked);
        return index < 0 ? null : accounts[index].Id;
    }
}
