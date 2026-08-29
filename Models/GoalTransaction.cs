using SQLite;

namespace Arthiva.Models;

public class GoalTransaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int GoalId { get; set; }

    /// <summary>
    /// Amount added or withdrawn from goal
    /// </summary>
    public decimal Amount { get; set; }

    [Indexed]
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Main Transaction Table Reference
    /// </summary>
    public int? TransactionId { get; set; }

    /// <summary>
    /// Contribution / Withdrawal
    /// </summary>
    [MaxLength(50)]
    public string TransactionType { get; set; } = "Contribution";

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}