namespace MoneySpend.Services;

/// <summary>A Borrow/Lend record that still has money pending.</summary>
public sealed record OpenRecord(int Id, string Type, decimal Pending, DateTime GivenDate);

/// <param name="RecordId">The record this step applies to (null = a NEW record is created)</param>
/// <param name="Kind">Receive | Return | PartialReturn | NewLend | NewBorrow</param>
public sealed record PlanStep(int? RecordId, string Kind, decimal Amount);

/// <summary>
/// Pure logic: where does one payment to / from a person go?
///   money OUT  -> first pays back what you OWE them (oldest Borrow first); anything left becomes a NEW Lend.
///   money IN   -> first settles what they OWE you (oldest Lend first);   anything left becomes a NEW Borrow.
/// </summary>
public static class PersonPaymentPlanner
{
    public static List<PlanStep> Plan(bool isCredit, decimal amount, IEnumerable<OpenRecord> open)
    {
        var steps = new List<PlanStep>();
        var remaining = amount;

        // credit settles Lend records, debit settles Borrow records
        var wanted = isCredit ? "Lend" : "Borrow";

        foreach (var record in open
                     .Where(r => r.Type == wanted && r.Pending > 0)
                     .OrderBy(r => r.GivenDate)
                     .ThenBy(r => r.Id))
        {
            if (remaining <= 0) break;

            var apply = Math.Min(remaining, record.Pending);

            string kind;
            if (isCredit)
                kind = "Receive";
            else
                kind = apply >= record.Pending ? "Return" : "PartialReturn";

            steps.Add(new PlanStep(record.Id, kind, apply));
            remaining -= apply;
        }

        // more than what was owed (or nothing was owed): the rest is a new loan the other way round
        if (remaining > 0)
            steps.Add(new PlanStep(null, isCredit ? "NewBorrow" : "NewLend", remaining));

        return steps;
    }
}
