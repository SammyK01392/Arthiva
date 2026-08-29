
using Contact = Arthiva.Models.Contact;

namespace Arthiva.Services;

public interface IContactService
{
    Task<List<Contact>> GetAllAsync();

    Task<List<Contact>> GetFavoritesAsync();

    Task<List<Contact>> SearchAsync(string query);

    Task<Contact?> GetByIdAsync(int id);

    Task<int> CreateAsync(Contact contact);

    Task<int> UpdateAsync(Contact contact);

    Task<int> SoftDeleteAsync(int id);

    Task ToggleFavoriteAsync(int id);
}