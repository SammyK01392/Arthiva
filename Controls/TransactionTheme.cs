namespace MoneySpend.Controls;

public enum TransactionType { Income, Expense, Borrow, Lend, Emi, Bill, Split }

public enum TransactionMode { Create, Edit }

/// <summary>Ek transaction type ka poora visual config.</summary>
public sealed record TransactionTheme(
    string Title,          // "Money In"
    string IconPath,       // main circle icon (Material 24x24 path)
    string AmountIconPath, // amount box ka chhota icon
    string AmountSign,     // "+" ya "-"
    Color Accent,          // main circle, amount text, wave
    Color AccentSoft,      // outer ring, border, icon bg
    Color AccentBg);       // amount box background

public static class TransactionThemes
{
    // Material Design icon paths (24x24 viewBox)
    private const string ArrowUp   = "M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z";
    private const string ArrowDown = "M20 12l-1.41-1.41L13 16.17V4h-2v12.17l-5.58-5.59L4 12l8 8 8-8z";
    private const string Person    = "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z";
    private const string Group     = "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z";
    private const string Calendar  = "M20 3h-1V1h-2v2H7V1H5v2H4c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 18H4V8h16v13z";
    private const string Document  = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";
    private const string Wallet    = "M21 18v1c0 1.1-.9 2-2 2H5c-1.11 0-2-.9-2-2V5c0-1.1.89-2 2-2h14c1.1 0 2 .9 2 2v1h-9c-1.11 0-2 .9-2 2v8c0 1.1.89 2 2 2h9zm-9-2h10V8H12v8zm4-2.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z";

    private static readonly Dictionary<TransactionType, TransactionTheme> Map = new()
    {
        [TransactionType.Income]  = new("Money In",  ArrowUp,   Wallet,   "+", Color.FromArgb("#22C55E"), Color.FromArgb("#D1FAE5"), Color.FromArgb("#ECFDF5")),
        [TransactionType.Expense] = new("Money Out", ArrowDown, Wallet,   "-", Color.FromArgb("#EF4444"), Color.FromArgb("#FEE2E2"), Color.FromArgb("#FEF2F2")),
        [TransactionType.Borrow]  = new("Borrowed",  Group,     Person,   "-", Color.FromArgb("#7C3AED"), Color.FromArgb("#EDE9FE"), Color.FromArgb("#F5F3FF")),
        [TransactionType.Lend]    = new("Lent",      Person,    Person,   "+", Color.FromArgb("#14B8A6"), Color.FromArgb("#CCFBF1"), Color.FromArgb("#F0FDFA")),
        [TransactionType.Emi]     = new("EMI",       Calendar,  Calendar, "-", Color.FromArgb("#2563EB"), Color.FromArgb("#DBEAFE"), Color.FromArgb("#EFF6FF")),
        [TransactionType.Bill]    = new("Bill",      Document,  Document, "-", Color.FromArgb("#F97316"), Color.FromArgb("#FFEDD5"), Color.FromArgb("#FFF7ED")),
        [TransactionType.Split]   = new("Split",     Group,     Group,    "-", Color.FromArgb("#EC4899"), Color.FromArgb("#FCE7F3"), Color.FromArgb("#FDF2F8")),
    };

    public static TransactionTheme Get(TransactionType type) => Map[type];

    /// <summary>Naya type ya override: ek line me.</summary>
    public static void Register(TransactionType type, TransactionTheme theme) => Map[type] = theme;
}
