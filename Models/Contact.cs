using SQLite;

namespace Arthiva.Models;

public class Contact
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Indexed]
    [MaxLength(15)]
    public string? Mobile { get; set; }

    [Indexed]
    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? UpiId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// Friend, Family, Customer, Vendor etc.
    /// </summary>
    [MaxLength(50)]
    public string ContactType { get; set; } = "Personal";

    public string? ProfileImage { get; set; }

    public bool IsFavorite { get; set; } = false;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ⬇️ NAYA: computed property (not persisted)
    /// <summary>First letter of name for avatar fallback.</summary>
    [Ignore]
    public string Initial =>
        string.IsNullOrWhiteSpace(Name)
            ? "?"
            : Name.Trim()[0].ToString().ToUpper();

    // ⬇️ NAYA: has profile image helper
    [Ignore]
    public bool HasProfileImage =>
        !string.IsNullOrWhiteSpace(ProfileImage);
}