using System.Globalization;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>Small shared dialogs for the Split screens (amount prompt, account picker, alert).</summary>
public static class SplitPrompts
{
    public static Task AlertAsync(string message)
        => Shell.Current.DisplayAlert("Split", message, "OK");

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
