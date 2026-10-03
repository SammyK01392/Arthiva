using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.ViewModels;

/// <summary>
/// Display wrapper combining a BorrowLend record with its contact's name,
/// so the list page can bind ContactName directly instead of calling a
/// lookup method from XAML (which isn't supported by data binding).
/// </summary>
public class BorrowLendListItem
{
    public BorrowLend Record { get; init; } = null!;
    public string ContactName { get; init; } = "Unknown";
    public string ContactInitial =>
    string.IsNullOrWhiteSpace(ContactName)
        ? "?"
        : ContactName.Trim()[0].ToString().ToUpper();
}

public partial class BorrowLendListViewModel : BaseViewModel
{
    private readonly IBorrowLendService _borrowLendService;
    private readonly IContactService _contactService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<BorrowLendListItem> Records { get; } = new();

    [ObservableProperty]
    private string selectedTab = "All"; // All / Lend / Borrow

    public List<string> Tabs { get; } = new() { "All", "Lend", "Borrow" };

    // CHANGED: tab badalne par ab guard-free, serialized refresh chalta hai
    partial void OnSelectedTabChanged(string value)
        => _autoRefresh.Request();

    public BorrowLendListViewModel(IBorrowLendService borrowLendService, IContactService contactService)
    {
        _borrowLendService = borrowLendService;
        _contactService = contactService;
        Title = "Borrow / Lend";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    [RelayCommand]
    private void SetTab(string tab)
        => SelectedTab = tab;

    [ObservableProperty]
    private decimal totalYouWillReceive; // sum of pending on your "Lend" records

    [ObservableProperty]
    private decimal totalYouOwe; // sum of pending on your "Borrow" records

    // CHANGED: asli load logic yahan (silent, spinner nahi).
    // Pehle saara data await karo, phir Clear + Add ek saath (bina await ke),
    // taaki do reload kabhi beech mein na ghus sakein.
    private async Task ReloadAsync()
    {
        var contacts = await _contactService.GetAllAsync();
        var contactLookup = contacts.ToDictionary(c => c.Id, c => c);

        // Ek hi DB read: summary ke liye sab, list ke liye tab ke hisaab se filter
        var everything = await _borrowLendService.GetAllAsync(null, includeClosed: false);

        var filtered = SelectedTab == "All"
            ? everything
            : everything.Where(r => r.Type == SelectedTab).ToList();

        Records.Clear();
        foreach (var r in filtered)
        {
            var name = contactLookup.TryGetValue(r.ContactId, out var contact) ? contact.Name : "Unknown";
            Records.Add(new BorrowLendListItem { Record = r, ContactName = name });
        }

        // Net summary across everything (independent of the active tab filter)
        // so switching tabs doesn't make these numbers flicker between partial views.
        TotalYouWillReceive = everything.Where(r => r.Type == "Lend").Sum(r => r.PendingAmount);
        TotalYouOwe = everything.Where(r => r.Type == "Borrow").Sum(r => r.PendingAmount);
    }

    // CHANGED: command ab ReloadAsync use karta hai, aur pehli load ke baad auto-refresh on
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(BorrowLendEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToRecordTransactionAsync(BorrowLendListItem item)
    {
        var route = nameof(RecordBorrowLendTransactionViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?BorrowLendId={item.Record.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(BorrowLendListItem item)
    {
        await ExecuteAsync(async () =>
        {
            await _borrowLendService.SoftDeleteAsync(item.Record.Id);
            Records.Remove(item); // turant gayab ho
            // CHANGED: totals Step 2 ke publish se auto-refresh khud theek kar dega
        });
    }
}