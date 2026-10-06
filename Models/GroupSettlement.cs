using SQLite;

namespace MoneySpend.Models;

/// <summary>
/// "From paid To" between two OTHER members of a group (neither is you). Only the group's
/// balances change — no account is touched. Settlements involving you go through
/// BorrowLend instead, so there is still a single money ledger for you.
/// </summary>
public class GroupSettlement
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int GroupId { get; set; }

    public int FromContactId { get; set; }

    public int ToContactId { get; set; }

    public decimal Amount { get; set; }

    public DateTime SettledDate { get; set; } = DateTime.Now;

    [MaxLength(200)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
