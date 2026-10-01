namespace Arthiva.Models;

/// <summary>
/// Local sync state of a record. Stored as an integer in SQLite.
/// Pending = 0 is intentional: any pre-existing row that gets this column
/// added via SQLite auto-migration (old installs) reads back as Pending by
/// default — exactly the behaviour we want, since those rows genuinely
/// haven't been synced to Firestore yet. No separate backfill of this field
/// is required.
/// </summary>
public enum SyncStatus
{
    Pending = 0,
    Synced = 1,
    Failed = 2
}
