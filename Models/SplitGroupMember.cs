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
}
