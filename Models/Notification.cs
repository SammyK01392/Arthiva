using SQLite;

namespace Arthiva.Models;

public class Notification
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Body { get; set; } = string.Empty;

    [Indexed]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// EMI, Bill, BorrowLend, Goal etc.
    /// </summary>
    public int? ReferenceId { get; set; }

    [Indexed]
    public DateTime ReminderDate { get; set; }

    public bool IsRead { get; set; } = false;

    public bool IsCompleted { get; set; } = false;

    public bool IsScheduled { get; set; } = true;

    public string? ActionRoute { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}