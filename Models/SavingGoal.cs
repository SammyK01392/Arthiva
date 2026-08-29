using SQLite;

namespace Arthiva.Models;

public class SavingGoal
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public decimal TargetAmount { get; set; }

    public decimal SavedAmount { get; set; }

    public DateTime? TargetDate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Active";

    /// <summary>
    /// Goal Icon (Car, Bike, House, Travel etc.)
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Goal Color for UI
    /// </summary>
    public string? ColorCode { get; set; }

    /// <summary>
    /// Auto calculated in UI
    /// </summary>
    public decimal RemainingAmount => TargetAmount - SavedAmount;

    public bool IsCompleted { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}