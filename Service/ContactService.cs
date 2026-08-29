// Explicit alias avoids the ambiguity with Microsoft.Maui.ApplicationModel
// .Communication.Contact, which .NET MAUI adds as a global using in every file.
using Contact = Arthiva.Models.Contact;

namespace Arthiva.Services;

public class ContactService : IContactService
{
    private readonly IGenericRepository<Contact> _repo;

    public ContactService(IGenericRepository<Contact> repo)
    {
        _repo = repo;
    }

    public async Task<List<Contact>> GetAllAsync()
    {
        var contacts = await _repo.FindAsync(c => !c.IsDeleted);
        return contacts.OrderBy(c => c.Name).ToList();
    }

    public async Task<List<Contact>> GetFavoritesAsync()
    {
        var contacts = await _repo.FindAsync(c => !c.IsDeleted && c.IsFavorite);
        return contacts.OrderBy(c => c.Name).ToList();
    }

    public async Task<List<Contact>> SearchAsync(string query)
    {
        var all = await GetAllAsync();
        if (string.IsNullOrWhiteSpace(query)) return all;

        query = query.Trim().ToLowerInvariant();
        return all.Where(c =>
                c.Name.ToLowerInvariant().Contains(query) ||
                (c.Mobile != null && c.Mobile.Contains(query)) ||
                (c.Email != null && c.Email.ToLowerInvariant().Contains(query)))
            .ToList();
    }

    public Task<Contact?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public Task<int> CreateAsync(Contact contact)
    {
        contact.CreatedAt = DateTime.UtcNow;
        contact.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(contact);
    }

    public Task<int> UpdateAsync(Contact contact)
    {
        contact.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(contact);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var contact = await _repo.GetByIdAsync(id);
        if (contact is null) return 0;

        contact.IsDeleted = true;
        contact.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(contact);
    }

    public async Task ToggleFavoriteAsync(int id)
    {
        var contact = await _repo.GetByIdAsync(id);
        if (contact is null) return;

        contact.IsFavorite = !contact.IsFavorite;
        contact.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(contact);
    }
}