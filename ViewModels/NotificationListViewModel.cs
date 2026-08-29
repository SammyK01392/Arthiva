using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class NotificationListViewModel : BaseViewModel
{
    private readonly INotificationService _notificationService;

    public ObservableCollection<Notification> Notifications { get; } = new();

    [ObservableProperty]
    private bool showUnreadOnly;

    partial void OnShowUnreadOnlyChanged(bool value)
        => _ = LoadAsync();

    public NotificationListViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
        Title = "Notifications";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var notifications = ShowUnreadOnly
                ? await _notificationService.GetUnreadAsync()
                : await _notificationService.GetAllAsync();

            Notifications.Clear();
            foreach (var n in notifications)
                Notifications.Add(n);
        });
    }

    [RelayCommand]
    private async Task OpenAsync(Notification notification)
    {
        await ExecuteAsync(async () =>
        {
            if (!notification.IsRead)
            {
                await _notificationService.MarkReadAsync(notification.Id);
                notification.IsRead = true;
            }

            if (!string.IsNullOrWhiteSpace(notification.ActionRoute))
                await Shell.Current.GoToAsync(notification.ActionRoute);
        });
    }

    [RelayCommand]
    private async Task MarkCompletedAsync(Notification notification)
    {
        await ExecuteAsync(async () =>
        {
            await _notificationService.MarkCompletedAsync(notification.Id);
            notification.IsCompleted = true;
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(Notification notification)
    {
        await ExecuteAsync(async () =>
        {
            await _notificationService.SoftDeleteAsync(notification.Id);
            Notifications.Remove(notification);
        });
    }
}
