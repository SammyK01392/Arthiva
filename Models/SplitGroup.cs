using SQLite;

namespace MoneySpend.Models;

/// <summary>A set of friends who share expenses (Goa Trip, Flatmates...). You are always an implicit member.</summary>
public class SplitGroup
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
