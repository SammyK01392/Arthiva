namespace MoneySpend.Data;

public static class DatabaseConstants
{
    public const string DatabaseFileName = "MoneySpend.db3";

    public const SQLite.SQLiteOpenFlags Flags =
        SQLite.SQLiteOpenFlags.ReadWrite |
        SQLite.SQLiteOpenFlags.Create |
        SQLite.SQLiteOpenFlags.SharedCache;
}