using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.ViewModels;

public partial class ContactListViewModel : BaseViewModel
{
    private readonly IContactService _contactService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<Contact> Contacts { get; } = new();

    [ObservableProperty]
    private string searchText = string.Empty;

    // CHANGED: search badalne par guard-free, serialized refresh
    partial void OnSearchTextChanged(string value)
        => _autoRefresh.Request();

    public ContactListViewModel(IContactService contactService)
    {
        _contactService = contactService;
        Title = "Contacts";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // CHANGED: asli load logic yahan (silent, spinner nahi)
    private async Task ReloadAsync()
    {
        var contacts = await _contactService.SearchAsync(SearchText);
        Contacts.Clear();
        foreach (var c in contacts)
            Contacts.Add(c);
    }

    // CHANGED: command ab ReloadAsync use karta hai, pehli load ke baad auto-refresh on
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(ContactEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToDetailAsync(Contact contact)
    {
        var route = nameof(ContactDetailViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?ContactId={contact.Id}");
    }

    [RelayCommand]
    private static async Task GoToEditAsync(Contact contact)
    {
        var route = nameof(ContactEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?ContactId={contact.Id}");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Contact contact)
    {
        await ExecuteAsync(async () =>
        {
            await _contactService.ToggleFavoriteAsync(contact.Id);
            contact.IsFavorite = !contact.IsFavorite;
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(Contact contact)
    {
        await ExecuteAsync(async () =>
        {
            await _contactService.SoftDeleteAsync(contact.Id);
            Contacts.Remove(contact); // turant gayab ho; baaki refresh auto-publish se
        });
    }
}