using SQLite;

namespace Arthiva.Models;

public class UserProfile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Indexed]
    [MaxLength(15)]
    public string MobileNo { get; set; } = string.Empty;

    [Indexed]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "INR";

    public string PasswordHash { get; set; } = string.Empty;

    public string? ProfileImage { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}