using SQLite;

namespace MoneySpend.Models;

public class SplitGroupMember
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int GroupId { get; set; }

    [Indexed]
    public int ContactId { get; set; }

    /// <summary>Soft-removed from the group (only allowed when they have no history in it).</summary>
    public bool IsRemoved { get; set; } = false;
}
