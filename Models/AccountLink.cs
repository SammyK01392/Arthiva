using SQLite;

namespace MoneySpend.Models;

/// <summary>
/// Remembers which account an account number belongs to ("9847" -> "IDBI Bank ••9847"),
/// so every statement / SMS payment goes to the right account.
/// </summary>
public class AccountLink
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Last 3-4 digits of the account / card (or "GIFT" for the PhonePe gift card).</summary>
    [Indexed, MaxLength(20)]
    public string Hint { get; set; } = string.Empty;

    public int AccountId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
