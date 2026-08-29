using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = Arthiva.Models.Contact;

namespace Arthiva.ViewModels;

/// <summary>
/// Display wrapper combining a BorrowLend record with its contact's name,
/// so the list page can bind ContactName directly instead of calling a
/// lookup method from XAML (which isn't supported by data binding).
/// </summary>
public class BorrowLendListItem
{
    public BorrowLend Record { get; init; } = null!;
    public string ContactName { get; init; } = "Unknown";
}

public partial class BorrowLendListViewModel : BaseViewModel
{
    private readonly IBorrowLendService _borrowLendService;
    private readonly IContactService _contactService;

    public ObservableCollection<BorrowLendListItem> Records { get; } = new();

    [ObservableProperty]
    private string selectedTab = "Lend"; // Lend / Borrow

    public List<string> Tabs { get; } = new() { "Lend", "Borrow" };

    partial void OnSelectedTabChanged(string value)
        => _ = LoadAsync();

    public BorrowLendListViewModel(IBorrowLendService borrowLendService, IContactService contactService)
    {
        _borrowLendService = borrowLendService;
        _contactService = contactService;
        Title = "Borrow / Lend";
    }

    [RelayCommand]
    private void SetTab(string tab)
        => SelectedTab = tab;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var contacts = await _contactService.GetAllAsync();
            var contactLookup = contacts.ToDictionary(c => c.Id, c => c);

            var records = await _borrowLendService.GetAllAsync(SelectedTab, includeClosed: false);

            Records.Clear();
            foreach (var r in records)
            {
                var name = contactLookup.TryGetValue(r.ContactId, out var contact) ? contact.Name : "Unknown";
                Records.Add(new BorrowLendListItem { Record = r, ContactName = name });
            }
        });
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
            Records.Remove(item);
        });
    }
}
