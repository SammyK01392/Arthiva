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

    public ObservableCollection<string> MovementTypes { get; } = new();

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
            var types = Record.Type == "Lend"
                ? new[] { "Receive", "PartialReturn", "Lend" }
                : new[] { "Return", "PartialReturn", "Borrow" };
            foreach (var t in types)
                MovementTypes.Add(t);

            MovementType = MovementTypes.First();

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

        var isReturnMovement = MovementType is "Return" or "Receive" or "PartialReturn";
        if (isReturnMovement && Amount > Record.PendingAmount)
        {
            ErrorMessage = $"Amount can't exceed the outstanding balance of ₹{Record.PendingAmount:N2}.";
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

            var result = await _borrowLendService.RecordTransactionAsync(txn, SelectedAccount?.Id);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Couldn't save this transaction.";
                return;
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
