using SQLite;

namespace Arthiva.Models;

public class Account
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Cash, Bank, Wallet, CreditCard
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    public decimal OpeningBalance { get; set; }

    public decimal CurrentBalance { get; set; }

    [MaxLength(20)]
    public string? ColorCode { get; set; }

    [MaxLength(100)]
    public string? Icon { get; set; }

    /// <summary>
    /// SBI, HDFC, ICICI, Axis
    /// </summary>
    [MaxLength(100)]
    public string? BankName { get; set; }

    /// <summary>
    /// Only masked value
    /// Example: XXXX4567
    /// </summary>
    [MaxLength(20)]
    public string? AccountNumber { get; set; }

    [MaxLength(100)]
    public string? UpiId { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}