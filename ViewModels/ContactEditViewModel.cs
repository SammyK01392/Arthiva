using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Services;
// Explicit alias avoids ambiguity with MAUI's own Contact type, which .NET
// MAUI adds as a global using in every file in the project.
using Contact = Arthiva.Models.Contact;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(ContactId), "ContactId")]
public partial class ContactEditViewModel : BaseViewModel
{
    private readonly IContactService _contactService;

    private int _contactId;
    private Contact? _existingContact;

    public string ContactId
    {
        set => _contactId = int.TryParse(value, out var id) ? id : 0;
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? mobile;

    [ObservableProperty]
    private string? email;

    [ObservableProperty]
    private string? address;

    [ObservableProperty]
    private string? upiId;

    [ObservableProperty]
    private string contactType = "Personal";

    [ObservableProperty]
    private string? notes;

    public List<string> ContactTypes { get; } = new() { "Personal", "Family", "Friend", "Customer", "Vendor" };

    public ContactEditViewModel(IContactService contactService)
    {
        _contactService = contactService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        if (_contactId <= 0)
        {
            Title = "Add Contact";
            IsEditMode = false;
            _existingContact = null;
            Name = string.Empty;
            Mobile = null;
            Email = null;
            Address = null;
            UpiId = null;
            ContactType = "Personal";
            Notes = null;
            ErrorMessage = null;
            return;
        }

        await ExecuteAsync(async () =>
        {
            _existingContact = await _contactService.GetByIdAsync(_contactId);
            if (_existingContact is null) return;

            Title = "Edit Contact";
            IsEditMode = true;
            Name = _existingContact.Name;
            Mobile = _existingContact.Mobile;
            Email = _existingContact.Email;
            Address = _existingContact.Address;
            UpiId = _existingContact.UpiId;
            ContactType = _existingContact.ContactType;
            Notes = _existingContact.Notes;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Enter a contact name.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingContact is not null)
            {
                _existingContact.Name = Name;
                _existingContact.Mobile = Mobile;
                _existingContact.Email = Email;
                _existingContact.Address = Address;
                _existingContact.UpiId = UpiId;
                _existingContact.ContactType = ContactType;
                _existingContact.Notes = Notes;

                await _contactService.UpdateAsync(_existingContact);
            }
            else
            {
                var contact = new Contact
                {
                    Name = Name,
                    Mobile = Mobile,
                    Email = Email,
                    Address = Address,
                    UpiId = UpiId,
                    ContactType = ContactType,
                    Notes = Notes
                };
                await _contactService.CreateAsync(contact);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
