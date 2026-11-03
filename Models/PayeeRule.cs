using SQLite;

namespace MoneySpend.Models;

/// <summary>
/// Remembers what the user decided for a payee, so the next SMS from the
/// same UPI id / name is filled in automatically (name + category).
/// </summary>
public class PayeeRule
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, MaxLength(150)]
    public string MatchKey { get; set; } = string.Empty;

    /// <summary>Separate rules for money IN and money OUT (categories differ).</summary>
    public bool IsCredit { get; set; }

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public int? CategoryId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
