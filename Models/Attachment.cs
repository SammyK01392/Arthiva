using SQLite;

namespace Arthiva.Models;

public class Attachment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Transaction, Bill, EMI, BorrowLend, Goal etc.
    /// </summary>
    [Indexed]
    [MaxLength(50)]
    public string ReferenceType { get; set; } = string.Empty;

    [Indexed]
    public int ReferenceId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ContentType { get; set; }

    /// <summary>
    /// File size in KB
    /// </summary>
    public long FileSize { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}