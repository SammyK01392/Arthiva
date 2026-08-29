using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(BorrowLendId), "BorrowLendId")]
public partial class RecordBorrowLendTransactionViewModel : BaseViewModel
{
    private readonly IBorrowLendService _borrowLendService;
    private readonly IContactService _contactService;
    private readonly IAccountService _accountService;

    private int _borrowLendId;

    public string BorrowLendId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _borrowLendId = id;
        }
    }

    [ObservableProperty]
    private BorrowLend? record;

    [ObservableProperty]
    private string contactName = string.Empty;

    [ObservableProperty]
    private string movementType = "Return";

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private DateTime transactionDate = DateTime.Now;

    [ObservableProperty]
    private string? paymentMethod = "Cash";

    [ObservableProperty]
    private string? referenceNo;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private Account? selectedAccount;

    public ObservableCollection<Account> Accounts { get; } = new();

    public List<string> MovementTypes { get; } = new();

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer" };

    public RecordBorrowLendTransactionViewModel(
        IBorrowLendService borrowLendService,
        IContactService contactService,
        IAccountService accountService)
    {
        _borrowLendService = borrowLendService;
        _contactService = contactService;
        _accountService = accountService;
        Title = "Record Transaction";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Record = await _borrowLendService.GetByIdAsync(_borrowLendId);
            if (Record is null) return;

            var contact = await _contactService.GetByIdAsync(Record.ContactId);
            ContactName = contact?.Name ?? "Unknown";

            MovementTypes.Clear();
            // "Return"/"Receive"/"PartialReturn" close the balance; the same
            // Type name as the record allows recording an additional advance.
            MovementTypes.AddRange(Record.Type == "Lend"
                ? new[] { "Receive", "PartialReturn", "Lend" }
                : new[] { "Return", "PartialReturn", "Borrow" });

            MovementType = MovementTypes.First();

            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Record is null || Amount <= 0)
        {
            ErrorMessage = "Enter a valid amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var txn = new BorrowLendTransaction
            {
                BorrowLendId = Record.Id,
                Amount = Amount,
                TransactionDate = TransactionDate,
                Type = MovementType,
                PaymentMethod = PaymentMethod,
                ReferenceNo = ReferenceNo,
                Notes = Notes
            };

            await _borrowLendService.RecordTransactionAsync(txn, SelectedAccount?.Id);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
