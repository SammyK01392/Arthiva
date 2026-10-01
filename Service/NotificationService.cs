using Arthiva.Models;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

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

    public async Task<int> CreateAsync(Notification notification)
    {
        notification.CreatedAt = DateTime.UtcNow;
        notification.UpdatedAt = DateTime.UtcNow;

        var id = await _repo.AddAsync(notification);

        // Schedule the actual OS-level notification so it fires at ReminderDate,
        // even if the app isn't open at that time. Creating the DB row alone
        // (as before) never triggers anything visible to the user.
        if (notification.IsScheduled && notification.ReminderDate > DateTime.Now)
        {
            notification.Id = id;
            await ScheduleOsNotificationAsync(notification);
        }

        return id;
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

        // Cancel the OS notification since the reminder is done — otherwise
        // it could still fire at ReminderDate even after being marked complete.
        LocalNotificationCenter.Current.Cancel(id);

        return await _repo.UpdateAsync(notification);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var notification = await _repo.GetByIdAsync(id);
        if (notification is null) return 0;

        notification.IsDeleted = true;
        notification.UpdatedAt = DateTime.UtcNow;

        LocalNotificationCenter.Current.Cancel(id);

        return await _repo.UpdateAsync(notification);
    }

    /// <summary>
    /// Re-schedules every pending (not completed/deleted, still in the future)
    /// notification's OS alarm. Call this once at app startup — Android can
    /// drop scheduled exact alarms across a device reboot or app force-stop,
    /// so DB rows can silently go out of sync with what the OS will actually fire.
    /// </summary>
    public async Task RescheduleAllPendingAsync()
    {
        var all = await _repo.FindAsync(n =>
            !n.IsDeleted && !n.IsCompleted && n.IsScheduled && n.ReminderDate > DateTime.Now);

        foreach (var notification in all)
        {
            await ScheduleOsNotificationAsync(notification);
        }
    }

    private static Task ScheduleOsNotificationAsync(Notification notification)
    {
        var request = new NotificationRequest
        {
            NotificationId = notification.Id,
            Title = notification.Title,
            Description = notification.Body,
            ReturningData = notification.ActionRoute, // tapping the notification can carry the route to navigate to
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = notification.ReminderDate
            }
        };

        return LocalNotificationCenter.Current.Show(request);
    }
}