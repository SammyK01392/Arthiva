using SQLite;

namespace Arthiva.Models;

public class RecurringTransaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int AccountId { get; set; }

    [Indexed]
    public int CategoryId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(50)]
    public string TransactionType { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Frequency { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime StartDate { get; set; }

    [Indexed]
    public DateTime NextRunDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public bool AutoCreateTransaction { get; set; } = true;

    /// <summary>
    /// Last generated transaction date
    /// </summary>
    public DateTime? LastExecutedDate { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
