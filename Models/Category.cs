using SQLite;

namespace Arthiva.Models;

public class Category
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Income / Expense
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Icon Name (FontAwesome, Material Icon etc.)
    /// </summary>
    [MaxLength(100)]
    public string? Icon { get; set; }

    /// <summary>
    /// Hex Color Code
    /// Example: #2196F3
    /// </summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Parent Category Support
    /// Food -> Fast Food
    /// Travel -> Fuel
    /// </summary>
    public int? ParentId { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Show category in dashboard quick add
    /// </summary>
    public bool IsFavorite { get; set; } = false;

    /// <summary>
    /// Sort order in UI
    /// </summary>
    public int DisplayOrder { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
