using SQLite;

namespace MoneySpend.Models;

public class Transaction
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

    [Indexed]
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Payee { get; set; }

    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    public bool IsRecurring { get; set; }

    /// <summary>
    /// EMI, Bill, BorrowLend etc. ka reference id
    /// </summary>
    public int? SourceReferenceId { get; set; }

    /// <summary>
    /// EMI, Bill, BorrowLend, Goal etc.
    /// </summary>
    [MaxLength(50)]
    public string? SourceType { get; set; }

    public string? AttachmentPath { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ═══════════════════════════════════════════════════
    //  CLOUD SYNC (Firestore) — added, existing columns untouched
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Stable cross-device identifier used as the Firestore document id
    /// (users/{firebaseUid}/transactions/{SyncId}). Deliberately separate
    /// from the local SQLite auto-increment Id, which is only meaningful
    /// on this one device/install.
    /// </summary>
    [Indexed]
    public string? SyncId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Local sync state. See remarks on <see cref="Models.SyncStatus"/> for
    /// why Pending (0) is a safe default for rows migrated from an older
    /// schema version too.
    /// </summary>
    [Indexed]
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
}