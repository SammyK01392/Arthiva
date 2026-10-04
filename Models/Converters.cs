using System.Globalization;
using MoneySpend.Models;

namespace MoneySpend.Converters;

/// <summary>Transaction (or a "Income"/"Expense" string) -> Income/Expense Color.</summary>
public class TransactionAmountColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value switch
        {
            Transaction t => t.TransactionType,
            string s => s,
            _ => null
        };

        return type == "Income"
            ? Application.Current?.Resources["Income"]
            : Application.Current?.Resources["Expense"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Transaction -> "+₹1,200.00" (Income) or "-₹450.00" (Expense).</summary>
public class TransactionAmountTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Transaction t) return string.Empty;

        var sign = t.TransactionType == "Income" ? "+" : "-";
        return $"{sign}₹{t.Amount:N2}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Plain decimal -> "₹1,200.00" (no sign), for balances/totals.</summary>
public class CurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? $"₹{d:N2}" : "₹0.00";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>Collection count (int) -> bool. Use with CollectionCount binding for empty-state visibility.</summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverse of CountToBoolConverter — true when the collection IS empty.</summary>
public class CountToInverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count == 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>int > 0 -> bool. Used to show/hide the notification badge dot.</summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Spent/Budget pair (MultiBinding: [SpentAmount, BudgetAmount]) -> 0.0–1.0 progress ratio.</summary>
public class SpentToBudgetRatioConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not decimal spent || values[1] is not decimal budget || budget <= 0)
            return 0d;

        var ratio = (double)(spent / budget);
        return Math.Clamp(ratio, 0d, 1d);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Budget spent ratio (0–1 double) -> Income/Warning/Expense Color based on how close to the limit.</summary>
public class BudgetRatioToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ratio = value is double d ? d : 0d;

        var key = ratio >= 1.0 ? "Expense" : ratio >= 0.8 ? "Warning" : "Income";
        return Application.Current?.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>BorrowLend "Lend"/"Borrow" -> Expense/Income Color (Lend = money out, Borrow = money in).</summary>
public class BorrowLendTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value as string;
        return type == "Lend"
            ? Application.Current?.Resources["Expense"]
            : Application.Current?.Resources["Income"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Paid/Total installment pair (MultiBinding: [PaidInstallment, TotalInstallment]) -> 0.0–1.0 progress ratio.</summary>
public class InstallmentRatioConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not int paid || values[1] is not int total || total <= 0)
            return 0d;

        return Math.Clamp((double)paid / total, 0d, 1d);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Any object -> bool (true when NOT null). Useful for optional DateTime?/reference fields.</summary>
public class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Spent/Budget pair (MultiBinding: [SpentAmount, BudgetAmount]) -> Income/Warning/Expense Color directly.</summary>
public class SpentToBudgetColorConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not decimal spent || values[1] is not decimal budget || budget <= 0)
            return Application.Current?.Resources["Income"];

        var ratio = (double)(spent / budget);
        var key = ratio >= 1.0 ? "Expense" : ratio >= 0.8 ? "Warning" : "Income";
        return Application.Current?.Resources[key];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>String is null/empty -> bool (true when it HAS content).</summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>String is null/whitespace -> bool (true when it's EMPTY).</summary>
public class StringEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Decimal > 0 -> bool. Used to show/hide optional penalty/fee amounts.</summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Hex string (#RRGGBB or #AARRGGBB) -> MAUI Color.</summary>
public class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Colors.Transparent;

        try
        {
            return Color.FromArgb(hex);
        }
        catch
        {
            return Colors.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Same hex -> same color with alpha overlay (default 20%). Use ConverterParameter="30" for 30%.</summary>
public class HexToTintConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Colors.Transparent;

        try
        {
            var color = Color.FromArgb(hex);
            var alphaPercent = 20;

            if (parameter is string s && int.TryParse(s, out var p))
                alphaPercent = Math.Clamp(p, 0, 100);

            return color.WithAlpha(alphaPercent / 100f);
        }
        catch
        {
            return Colors.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"Sameer Khan" -> "SK", "Sameer" -> "S". Used for contact avatars.</summary>
public class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 1
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
            : parts[0][0].ToString().ToUpper();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}