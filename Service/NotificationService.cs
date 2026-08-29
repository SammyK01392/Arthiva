using Arthiva.Models;

namespace Arthiva.Services;

public class NotificationService : INotificationService
{
    private readonly IGenericRepository<Notification> _repo;

    public NotificationService(IGenericRepository<Notification> repo)
    {
        _repo = repo;
    }

    public async Task<List<Notification>> GetAllAsync()
    {
        var all = await _repo.FindAsync(n => !n.IsDeleted);
        return all.OrderByDescending(n => n.ReminderDate).ToList();
    }

    public async Task<List<Notification>> GetUnreadAsync()
    {
        var all = await _repo.FindAsync(n => !n.IsDeleted && !n.IsRead);
        return all.OrderByDescending(n => n.ReminderDate).ToList();
    }

    public async Task<List<Notification>> GetDueAsync(DateTime? asOf = null)
    {
        var cutoff = asOf ?? DateTime.UtcNow;
        var due = await _repo.FindAsync(n =>
            !n.IsDeleted && n.IsScheduled && !n.IsCompleted && n.ReminderDate <= cutoff);
        return due.OrderBy(n => n.ReminderDate).ToList();
    }

    public Task<Notification?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public Task<int> CreateAsync(Notification notification)
    {
        notification.CreatedAt = DateTime.UtcNow;
        notification.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(notification);
    }

    public async Task<int> MarkReadAsync(int id)
    {
        var notification = await _repo.GetByIdAsync(id);
        if (notification is null) return 0;

        notification.IsRead = true;
        notification.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(notification);
    }

    public async Task<int> MarkCompletedAsync(int id)
    {
        var notification = await _repo.GetByIdAsync(id);
        if (notification is null) return 0;

        notification.IsCompleted = true;
        notification.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(notification);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var notification = await _repo.GetByIdAsync(id);
        if (notification is null) return 0;

        notification.IsDeleted = true;
        notification.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(notification);
    }
}
