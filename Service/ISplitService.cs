using System.Globalization;
using MoneySpend.Models;
// MAUI also has a "Contact" type — keep the alias (same reason as MoneySpendDatabase).
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public record SplitShareInput(int ContactId, decimal Amount);

public record SplitRequest(
    string Title,
    decimal TotalAmount,
    string Method,
    DateTime Date,
    int AccountId,
    int CategoryId,
    decimal MyShare,
    IReadOnlyList<SplitShareInput> Shares,
    string? Notes);

public record SplitResult(bool Success, string? ErrorMessage = null);

/// <summary>Row for the "History" tab.</summary>
public class SplitListItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public decimal Total { get; init; }
    public decimal MyShare { get; init; }
    public string FriendsText { get; init; } = string.Empty;
    public decimal Pending { get; init; }

    public bool IsSettled => Pending <= 0;
    public string StatusText => IsSettled ? "Settled" : $"₹{Pending:N2} pending";
}

/// <summary>Row for the "Friends" tab — net of all open Borrow/Lend records for one contact.</summary>
public class FriendBalance
{
    public int ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Mobile { get; init; }
    public decimal LendPending { get; init; }    // they owe you
    public decimal BorrowPending { get; init; }  // you owe them

    public decimal Net => LendPending - BorrowPending;
    public decimal AbsNet => Math.Abs(Net);
    public bool OwesYou => Net > 0;
    public bool CanSettle => LendPending > 0;
    public string StatusText => Net > 0 ? "owes you" : Net < 0 ? "you owe" : "settled up";

    public string Initial =>
        string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[0].ToString().ToUpper();
}

public static class SplitCalculator
{
    /// <summary>Parses user input with the device culture first, then invariant. Returns 0 on failure.</summary>
    public static decimal ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        text = text.Trim();

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return 0m;
    }

    /// <summary>Splits in whole paise so the parts always add up to the total (extra paise go to the first people).</summary>
    public static decimal[] Equal(decimal total, int count)
    {
        if (count <= 0) return Array.Empty<decimal>();

        var cents = (long)Math.Round(Math.Max(total, 0m) * 100m, 0, MidpointRounding.AwayFromZero);
        var baseCents = cents / count;
        var extra = (int)(cents % count);

        var result = new decimal[count];
        for (var i = 0; i < count; i++)
            result[i] = (baseCents + (i < extra ? 1 : 0)) / 100m;

        return result;
    }

    /// <summary>Percent split; if percents add up to exactly 100 the rounding leftover goes to the last person.</summary>
    public static decimal[] Percent(decimal total, IReadOnlyList<decimal> percents)
    {
        var result = percents
            .Select(p => Math.Round(total * p / 100m, 2, MidpointRounding.AwayFromZero))
            .ToArray();

        if (result.Length > 0 && percents.Sum() == 100m)
            result[^1] += total - result.Sum();

        return result;
    }

    /// <summary>1,200 or 1,200.50 — no needless ".00".</summary>
    public static string Money(decimal amount)
        => amount % 1m == 0m ? amount.ToString("N0") : amount.ToString("N2");
}

public interface ISplitService
{
    Task<List<Contact>> GetContactsAsync();

    /// <summary>
    /// "I paid" split. Creates: your share as an Expense, one BorrowLend (Lend)
    /// per friend (which also debits the account), and the Split records.
    /// All-or-nothing: if anything fails midway, what was created is reversed.
    /// </summary>
    Task<SplitResult> CreateAsync(SplitRequest request);

    /// <summary>Reverses everything the split created (including any settlements already received).</summary>
    Task<SplitResult> DeleteAsync(int splitId);

    Task<List<SplitListItem>> GetSplitsAsync();

    Task<List<FriendBalance>> GetFriendBalancesAsync();

    /// <summary>
    /// Records money received from a friend, applied to their oldest open Lend
    /// records first (partial amounts allowed). Credits the given account.
    /// </summary>
    Task<SplitResult> SettleAsync(int contactId, decimal amount, int accountId);

    /// <summary>Ready-to-send reminder text (WhatsApp / share sheet).</summary>
    Task<string> BuildReminderTextAsync(int contactId);
}
