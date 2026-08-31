using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = Arthiva.Models.Contact;

namespace Arthiva.ViewModels;

public partial class BorrowLendEditViewModel : BaseViewModel
{
    private readonly IBorrowLendService _borrowLendService;
    private readonly IContactService _contactService;
    private readonly IAccountService _accountService;

    [ObservableProperty]
    private string type = "Lend"; // Borrow / Lend

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
        IAccountService accountService)
    {
        _borrowLendService = borrowLendService;
        _contactService = contactService;
        _accountService = accountService;
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
