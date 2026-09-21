using System.Collections.ObjectModel;
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
        if (Contact is null)
            return;

#if ANDROID

        await ExecuteAsync(async () =>
        {
            var filePath =
                await PdfReportService.GenerateContactReportAsync(
                    Contact.Name,
                    Contact.Mobile,
                    Contact.Email,
                    TotalYouWillReceive,
                    TotalYouOwe,
                    NetBalanceLabel,
                    History);

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = $"Share report for {Contact.Name}",
                    File = new ShareFile(
                        filePath,
                        "application/pdf")
                });
        });

#else

    await Shell.Current.DisplayAlert(
        "Not Supported",
        "PDF sharing is currently supported on Android.",
        "OK");

#endif
    }
}
