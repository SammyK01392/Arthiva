using System.Globalization;
using MoneySpend.Models;
// MAUI also has a "Contact" type — keep the alias (same reason as MoneySpendDatabase).
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public record SplitShareInput(int ContactId, decimal Amount);

/// <summary>
/// PaidByContactId null = you paid. GroupId null = no group.
/// Shares = every selected friend except you (the payer included if they took a share).
/// </summary>
public record SplitRequest(
    string Title,
    decimal TotalAmount,
    string Method,
    DateTime Date,
    int AccountId,
    int CategoryId,
    decimal MyShare,
    IReadOnlyList<SplitShareInput> Shares,
    string? Notes,
    int? PaidByContactId = null,
    int? GroupId = null);

public record SplitResult(bool Success, string? ErrorMessage = null);

/// <summary>Result of quick-adding a friend from the Split screen.</summary>
public record AddFriendResult(
    bool Success,
    int ContactId,
    string Name,
    string? ErrorMessage = null,
    bool AlreadyExisted = false);

/// <summary>Row for expense lists (History tab / group detail).</summary>
public class SplitListItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public decimal Total { get; init; }
    public decimal MyShare { get; init; }

    /// <summary>"You paid • Goa Trip • with Rahul, Amit"</summary>
    public string SubtitleText { get; init; } = string.Empty;

    /// <summary>"Settled", "₹500.00 pending", "You owe ₹500.00" or "No dues for you".</summary>
    public string StatusText { get; init; } = string.Empty;
    public bool IsSettled { get; init; }
}

/// <summary>Row for the "Friends" tab — net of all open Borrow/Lend records for one contact.</summary>
public class FriendBalance
{
    public int ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Mobile { get; init; }
    public decimal LendPending { get; init; }    // they owe you
    public decimal BorrowPending { get; init; }  // you owe them
    public string? UpiId { get; init; }

    public decimal Net => LendPending - BorrowPending;
    public decimal AbsNet => Math.Abs(Net);
    public bool OwesYou => Net > 0;
    public bool CanSettle => LendPending > 0;
    public bool CanPay => BorrowPending > 0;
    public string StatusText => Net > 0 ? "owes you" : Net < 0 ? "you owe" : "settled up";

    public string Initial =>
        string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[0].ToString().ToUpper();
}

public static class SplitCalculator
{
    /// <summary>name@bank style UPI id.</summary>
    public static bool IsValidUpi(string? upiId)
        => !string.IsNullOrWhiteSpace(upiId)
           && upiId.Trim().Length <= 100
           && System.Text.RegularExpressions.Regex.IsMatch(
               upiId.Trim(), @"^[a-zA-Z0-9.\-_]{2,}@[a-zA-Z][a-zA-Z0-9]{1,}$");

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
    /// Quick-add a friend straight from a Split screen (name required, mobile optional).
    /// If a contact with the same name already exists it is returned instead of creating a duplicate.
    /// </summary>
    Task<AddFriendResult> AddFriendAsync(string name, string? mobile);

    /// <summary>
    /// Creates a split. YOU paid: your share = Expense, each friend = Lend (account debited).
    /// A FRIEND paid (cash-basis): nothing touches your account now; a Borrow record is made for
    /// your share and the expense is booked when you pay them back (PayAsync).
    /// All-or-nothing: if anything fails midway, what was created is reversed.
    /// </summary>
    Task<SplitResult> CreateAsync(SplitRequest request);

    /// <summary>Reverses everything the split created (including settlements already made).</summary>
    Task<SplitResult> DeleteAsync(int splitId);

    /// <summary>groupId null = all splits.</summary>
    Task<List<SplitListItem>> GetSplitsAsync(int? groupId = null);

    Task<List<FriendBalance>> GetFriendBalancesAsync();

    /// <summary>
    /// Money RECEIVED from a friend, applied to their oldest open Lend records first
    /// (partial allowed). groupId limits it to that group's bills. Credits the account.
    /// </summary>
    Task<SplitResult> SettleAsync(int contactId, decimal amount, int accountId, int? groupId = null);

    /// <summary>
    /// Money YOU PAY a friend, applied to your oldest open Borrow records with them first.
    /// For split-created Borrow records the payment is booked as a normal Expense in the
    /// split's category (this is the cash-basis moment). Debits the account.
    /// </summary>
    Task<SplitResult> PayAsync(int contactId, decimal amount, int accountId, int? groupId = null);

    /// <summary>Ready-to-send reminder text (WhatsApp / share sheet). myUpiId is appended when given.</summary>
    Task<string> BuildReminderTextAsync(int contactId, string? myUpiId = null);

    /// <summary>Shareable text receipt for one split (who paid, each person's share).</summary>
    Task<string> BuildReceiptAsync(int splitId);

    Task<string?> GetFriendUpiAsync(int contactId);

    /// <summary>Saves a friend's UPI id on their contact (validated).</summary>
    Task<SplitResult> SetFriendUpiAsync(int contactId, string upiId);
}
