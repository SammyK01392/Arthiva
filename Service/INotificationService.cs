using Arthiva.Models;

namespace Arthiva.Services;

public interface INotificationService
{
    Task<List<Notification>> GetAllAsync();

    Task<List<Notification>> GetUnreadAsync();

    /// <summary>Scheduled notifications due to fire on/before the given date.</summary>
    Task<List<Notification>> GetDueAsync(DateTime? asOf = null);

    Task<Notification?> GetByIdAsync(int id);

    Task<int> CreateAsync(Notification notification);

    Task<int> MarkReadAsync(int id);

    Task<int> MarkCompletedAsync(int id);

    Task<int> SoftDeleteAsync(int id);
}
