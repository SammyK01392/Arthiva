using System.Text.RegularExpressions;

namespace MoneySpend.Services;

/// <summary>An EMI that is already in the app.</summary>
public sealed record ExistingEmi(
    int Id, string Name, string? LoanNumber, decimal InstalmentAmount,
    int? TenureMonths, decimal? Principal, DateTime? StartDate);

/// <summary>A payment already recorded against an EMI.</summary>
public sealed record ExistingPayment(DateTime Date, decimal Amount);

public sealed record EmiMatch(int? EmiId, string Confidence, string Reason);

/// <summary>Pure logic: is this statement an EMI we already have, and which payments are still missing?</summary>
public static class EmiMatcher
{
    public static EmiMatch Find(EmiStatement st, IReadOnlyList<ExistingEmi> existing)
    {
        // 1. the loan account number is a perfect key
        var number = Normalize(st.LoanNumber);

        if (number.Length > 0)
        {
            var byNumber = existing.FirstOrDefault(e => Normalize(e.LoanNumber) == number);
            if (byNumber is not null)
                return new EmiMatch(byNumber.Id, "High", "Same loan account number");
        }

        // 2. no number saved: same lender + same instalment + same size / length / start
        var lenderWord = LenderWord(st.Lender);

        var candidates = existing
            .Where(e => Math.Abs(e.InstalmentAmount - st.InstalmentAmount) <= 1m)
            .Where(e => lenderWord is null || e.Name.Contains(lenderWord, StringComparison.OrdinalIgnoreCase))
            .Where(e =>
                (e.TenureMonths is int t && t == st.TenureMonths) ||
                (e.Principal is decimal p && Math.Abs(p - st.LoanAmount) <= 1m) ||
                (e.StartDate is DateTime s && st.LoanDate is DateTime l && Math.Abs((s - l).TotalDays) <= 45))
            .ToList();

        if (candidates.Count == 1)
            return new EmiMatch(candidates[0].Id, "Medium", "Same lender and instalment amount");

        return candidates.Count > 1
            ? new EmiMatch(null, "Low", "More than one EMI looks similar")
            : new EmiMatch(null, "High", "No such EMI yet");
    }

    /// <summary>Paid instalments of the statement that are NOT yet recorded (same amount, date within 5 days).</summary>
    public static List<EmiInstalment> MissingPayments(EmiStatement st, IEnumerable<ExistingPayment> recorded)
    {
        var left = recorded.ToList();
        var missing = new List<EmiInstalment>();

        foreach (var i in st.Instalments.Where(i => i.Paid && i.PaidDate is not null && i.PaidAmount is not null))
        {
            var hit = left.FindIndex(r =>
                Math.Abs(r.Amount - i.PaidAmount!.Value) <= 0.5m &&
                Math.Abs((r.Date.Date - i.PaidDate!.Value.Date).TotalDays) <= 5);

            if (hit >= 0) left.RemoveAt(hit);   // each recorded payment is used up only once
            else missing.Add(i);
        }

        return missing;
    }

    private static string Normalize(string? s) => Regex.Replace(s ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();

    /// <summary>"Bajaj Finance Limited" -> "Bajaj"</summary>
    private static string? LenderWord(string? lender)
        => lender?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.Length >= 3);
}
