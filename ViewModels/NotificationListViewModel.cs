using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class NotificationListViewModel : BaseViewModel
{
    private readonly INotificationService _notificationService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<Notification> Notifications { get; } = new();

    [ObservableProperty]
    private bool showUnreadOnly;

    // CHANGED: toggle par guard-free, serialized refresh
    partial void OnShowUnreadOnlyChanged(bool value)
        => _autoRefresh.Request();

    public NotificationListViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
        Title = "Notifications";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // CHANGED: asli load logic yahan (silent, spinner nahi)
    private async Task ReloadAsync()
    {
        var notifications = ShowUnreadOnly
            ? await _notificationService.GetUnreadAsync()
            : await _notificationService.GetAllAsync();

        Notifications.Clear();
        foreach (var n in notifications)
            Notifications.Add(n);
    }

    // CHANGED: command ab ReloadAsync use karta hai, pehli load ke baad auto-refresh on
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
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
            Notifications.Remove(notification); // turant gayab ho; baaki refresh auto-publish se
        });
    }
}