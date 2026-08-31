using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = Arthiva.Models.Contact;
using BorrowLend = Arthiva.Models.BorrowLend;

namespace Arthiva.ViewModels;

/// <summary>
/// Flattened history row: either the original loan/advance ("Initial ...",
/// TransactionId = null, not deletable) or a later Return/Receive/PartialReturn
/// (TransactionId set, deletable to correct a bad entry).
/// </summary>
public class ContactHistoryItem
{
    public int? TransactionId { get; init; }
    public string RecordType { get; init; } = string.Empty; // Lend / Borrow — which record this belongs to
    public string MovementLabel { get; init; } = string.Empty; // "Initial Lend", "Return", "PartialReturn"...
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
    public string? Notes { get; init; }
    public bool IsDeletable => TransactionId.HasValue;
}

[QueryProperty(nameof(ContactId), "ContactId")]
public partial class ContactDetailViewModel : BaseViewModel
{
    private readonly IContactService _contactService;
    private readonly IBorrowLendService _borrowLendService;

    private int _contactId;

    public string ContactId
    {
        set => _contactId = int.TryParse(value, out var id) ? id : 0;
    }

    [ObservableProperty]
    private Contact? contact;

    [ObservableProperty]
    private decimal totalYouWillReceive;

    [ObservableProperty]
    private decimal totalYouOwe;

    /// <summary>Clear, unambiguous summary — never a raw signed number.</summary>
    public string NetBalanceLabel
    {
        get
        {
            var diff = TotalYouWillReceive - TotalYouOwe;
            if (diff > 0.004m) return $"You should receive ₹{diff:N2}";
            if (diff < -0.004m) return $"You owe ₹{Math.Abs(diff):N2}";
            return "Settled";
        }
    }

    public Color NetBalanceColor
    {
        get
        {
            var diff = TotalYouWillReceive - TotalYouOwe;
            if (diff > 0.004m) return (Color)(Application.Current?.Resources["Income"] ?? Colors.Green);
            if (diff < -0.004m) return (Color)(Application.Current?.Resources["Expense"] ?? Colors.Red);
            return (Color)(Application.Current?.Resources["TextSecondary"] ?? Colors.Gray);
        }
    }

    public ObservableCollection<BorrowLend> Records { get; } = new();

    public ObservableCollection<ContactHistoryItem> History { get; } = new();

    public ContactDetailViewModel(IContactService contactService, IBorrowLendService borrowLendService)
    {
        _contactService = contactService;
        _borrowLendService = borrowLendService;
        Title = "Contact Details";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Contact = await _contactService.GetByIdAsync(_contactId);
            if (Contact is null) return;

            Title = Contact.Name;

            // Records, History, and the Summary cards are all derived from the
            // SAME freshly-loaded records + their transaction ledgers below —
            // no separate/cached totals, so they can never disagree.
            var records = await _borrowLendService.GetByContactAsync(_contactId);
            Records.Clear();
            foreach (var r in records)
                Records.Add(r);

            TotalYouWillReceive = records.Where(r => r.Type == "Lend").Sum(r => r.PendingAmount);
            TotalYouOwe = records.Where(r => r.Type == "Borrow").Sum(r => r.PendingAmount);
            OnPropertyChanged(nameof(NetBalanceLabel));
            OnPropertyChanged(nameof(NetBalanceColor));

            History.Clear();
            foreach (var record in records)
            {
                History.Add(new ContactHistoryItem
                {
                    RecordType = record.Type,
                    MovementLabel = $"Initial {record.Type}",
                    Amount = record.TotalAmount,
                    Date = record.GivenDate,
                    Notes = record.Notes
                });

                var txns = await _borrowLendService.GetTransactionsAsync(record.Id);
                foreach (var t in txns)
                {
                    History.Add(new ContactHistoryItem
                    {
                        TransactionId = t.Id,
                        RecordType = record.Type,
                        MovementLabel = t.Type,
                        Amount = t.Amount,
                        Date = t.TransactionDate,
                        Notes = t.Notes
                    });
                }
            }

            var ordered = History.OrderByDescending(h => h.Date).ToList();
            History.Clear();
            foreach (var h in ordered)
                History.Add(h);
        });
    }

    [RelayCommand]
    private static async Task GoToRecordTransactionAsync(BorrowLend record)
    {
        var route = nameof(RecordBorrowLendTransactionViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?BorrowLendId={record.Id}");
    }

    /// <summary>
    /// Corrects bad historical data (e.g. a Receive entered larger than what
    /// was ever lent) by removing that one movement and letting the parent
    /// record's PendingAmount/Status recompute from what remains.
    /// </summary>
    [RelayCommand]
    private async Task DeleteHistoryItemAsync(ContactHistoryItem item)
    {
        if (!item.IsDeletable || item.TransactionId is null) return;

        await ExecuteAsync(async () =>
        {
            await _borrowLendService.DeleteTransactionAsync(item.TransactionId.Value);
            await AppearingAsync();
        });
    }

    [RelayCommand]
    private async Task ShareReportAsync()
    {
        if (Contact is null) return;

        await ExecuteAsync(async () =>
        {
            var html = BuildReportHtml();

            var fileName = $"{Contact.Name.Replace(" ", "_")}_Report_{DateTime.Now:yyyyMMdd_HHmm}.html";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(filePath, html);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Share report for {Contact.Name}",
                File = new ShareFile(filePath)
            });
        });
    }

    private string BuildReportHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head><meta charset='utf-8'><title>Arthiva Statement</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;color:#0F172A;padding:24px;background:#F8FAFC;}");
        sb.AppendLine("h1{color:#F97316;margin-bottom:0;}");
        sb.AppendLine(".sub{color:#64748B;margin-top:4px;}");
        sb.AppendLine(".summary{display:flex;gap:16px;margin:20px 0;}");
        sb.AppendLine(".card{background:#fff;border:1px solid #E2E8F0;border-radius:12px;padding:16px;flex:1;}");
        sb.AppendLine(".card .label{color:#64748B;font-size:12px;}");
        sb.AppendLine(".card .value{font-size:20px;font-weight:bold;margin-top:4px;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:12px;background:#fff;}");
        sb.AppendLine("th,td{text-align:left;padding:10px;border-bottom:1px solid #E2E8F0;font-size:14px;}");
        sb.AppendLine("th{color:#64748B;font-size:12px;text-transform:uppercase;}");
        sb.AppendLine(".lend{color:#DC2626;} .borrow{color:#16A34A;}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>Arthiva Statement</h1>");
        sb.AppendLine($"<div class='sub'>Contact: {Contact!.Name}");
        if (!string.IsNullOrWhiteSpace(Contact.Mobile)) sb.Append($" • {Contact.Mobile}");
        if (!string.IsNullOrWhiteSpace(Contact.Email)) sb.Append($" • {Contact.Email}");
        sb.AppendLine("</div>");
        sb.AppendLine($"<div class='sub'>Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt}</div>");

        sb.AppendLine("<div class='summary'>");
        sb.AppendLine($"<div class='card'><div class='label'>You'll Receive</div><div class='value' style='color:#16A34A'>₹{TotalYouWillReceive:N2}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>You Owe</div><div class='value' style='color:#DC2626'>₹{TotalYouOwe:N2}</div></div>");
        sb.AppendLine($"<div class='card'><div class='label'>Net</div><div class='value'>{NetBalanceLabel}</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h3>Transaction History</h3>");
        sb.AppendLine("<table><tr><th>Date</th><th>Type</th><th>Movement</th><th>Amount</th><th>Notes</th></tr>");
        foreach (var h in History.OrderByDescending(h => h.Date))
        {
            var cssClass = h.RecordType == "Lend" ? "lend" : "borrow";
            sb.AppendLine($"<tr><td>{h.Date:dd MMM yyyy}</td><td class='{cssClass}'>{h.RecordType}</td>" +
                          $"<td>{h.MovementLabel}</td><td>₹{h.Amount:N2}</td><td>{h.Notes}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<p class='sub' style='margin-top:24px'>Generated by Arthiva</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
