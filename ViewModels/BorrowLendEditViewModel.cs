using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.ViewModels;

[QueryProperty(nameof(RequestedType), "Type")]
public partial class BorrowLendEditViewModel : BaseViewModel
{
    private readonly IBorrowLendService _borrowLendService;
    private readonly IContactService _contactService;
    private readonly IAccountService _accountService;
    private readonly ISharedRequestService _sharedRequests; // NEW

    [ObservableProperty]
    private string type = "Lend"; // Borrow / Lend

    // Set when navigated here via a Quick Action ("Borrow" or "Lend" button
    // on the Dashboard), so the toggle opens pre-set on the right side
    // instead of always defaulting to "Lend". Same Shell quirk as elsewhere:
    // resets to "" (not null) when the route is reached without this param.
    public string RequestedType
    {
        set
        {
            if (value is "Borrow" or "Lend")
                Type = value;
        }
    }

    [ObservableProperty]
    private decimal totalAmount;

    [ObservableProperty]
    private DateTime givenDate = DateTime.Now;

    [ObservableProperty]
    private DateTime? dueDate;

    [ObservableProperty]
    private string? paymentMethod = "Cash";

    [ObservableProperty]
    private bool reminderEnabled = true;

    [ObservableProperty]
    private int reminderBeforeDays = 3;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private Contact? selectedContact;

    [ObservableProperty]
    private Account? selectedAccount;

    public ObservableCollection<Contact> Contacts { get; } = new();

    public ObservableCollection<Account> Accounts { get; } = new();

    public List<string> Types { get; } = new() { "Lend", "Borrow" };

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer" };

    public BorrowLendEditViewModel(
        IBorrowLendService borrowLendService,
        IContactService contactService,
        IAccountService accountService,
        ISharedRequestService sharedRequests)
    {
        _borrowLendService = borrowLendService;
        _contactService = contactService;
        _accountService = accountService;
        _sharedRequests = sharedRequests;
        Title = "New Borrow / Lend";
    }

    [RelayCommand]
    private void SetType(string type)
        => Type = type;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var contacts = await _contactService.GetAllAsync();
            Contacts.Clear();
            foreach (var c in contacts)
                Contacts.Add(c);

            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
        });
    }

    [RelayCommand]
    private static async Task GoToAddContactAsync()
        => await Shell.Current.GoToAsync(nameof(ContactEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedContact is null)
        {
            ErrorMessage = "Select a contact.";
            return;
        }

        if (TotalAmount <= 0)
        {
            ErrorMessage = "Enter a valid amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            // NEW: a contact who is a connected MoneySpend user can receive this as a shared request.
            var linkState = await _sharedRequests.GetLinkStateAsync(SelectedContact.Id);

            if (linkState == ContactLinkState.Connected)
            {
                var sendRequest = await Shell.Current.DisplayAlert(
                    $"Send to {SelectedContact.Name}?",
                    $"{SelectedContact.Name} uses MoneySpend. Send this as a request so it is recorded for both of you once they accept? " +
                    "Only the amount, date and type are shared, never your notes or account.",
                    "Send request", "Save only for me");

                if (sendRequest)
                {
                    var sent = await _sharedRequests.SendBorrowLendRequestAsync(
                        SelectedContact.Id, Type, TotalAmount, GivenDate, SelectedAccount?.Id);

                    if (!sent.Success)
                    {
                        ErrorMessage = sent.Message;
                        return;
                    }

                    if (!string.IsNullOrEmpty(sent.Message))
                        await Shell.Current.DisplayAlert("Shared request", sent.Message, "OK");

                    await Shell.Current.GoToAsync("..");
                    return;
                }
            }
            else if (linkState == ContactLinkState.Unknown)
            {
                var saveLocally = await Shell.Current.DisplayAlert(
                    "Can't reach the server",
                    $"{SelectedContact.Name} is a MoneySpend user, but you're offline so the request can't be sent. " +
                    "Save it only on this phone (it won't be shared)?",
                    "Save only for me", "Cancel");

                if (!saveLocally) return;
            }

            // Existing local-only flow (unchanged).
            var record = new BorrowLend
            {
                ContactId = SelectedContact.Id,
                Type = Type,
                TotalAmount = TotalAmount,
                GivenDate = GivenDate,
                DueDate = DueDate,
                PaymentMethod = PaymentMethod,
                ReminderEnabled = ReminderEnabled,
                ReminderBeforeDays = ReminderBeforeDays,
                Notes = Notes
            };

            await _borrowLendService.CreateAsync(record, SelectedAccount?.Id);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
