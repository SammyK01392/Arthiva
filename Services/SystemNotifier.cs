using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace MoneySpend.Services;

public enum AppNotificationKind
{
    General = 1,
    Bill = 2,
    Emi = 3,
    Budget = 4,
    Payment = 5,
    Goal = 6
}

/// <summary>
/// Android status-bar notifications (like WhatsApp). Scheduled ones are handed to
/// the phone's AlarmManager, so they appear even when the app is fully closed.
/// </summary>
public static class SystemNotifier
{
    // one channel per topic -> the user can mute e.g. only "Budget alerts" in phone settings
    public const string GeneralChannel = "ms_general";
    public const string BillsChannel = "ms_bills";
    public const string EmiChannel = "ms_emi";
    public const string BudgetChannel = "ms_budget";
    public const string PaymentsChannel = "ms_payments";

    // MoneySpend orange (#FF6B00) - tints the small icon and the app name
    private static readonly int AccentArgb = unchecked((int)0xFFFF6B00);

    // Resources/Images/ms_notify.png  (white shape on transparent background)
    private const string SmallIcon = "ms_notify";

    // reminders go out at 9:00 AM
    private const int ReminderHour = 9;

    // ─────────────────────────────────────────────────────────
    //  PERMISSION  (Android 13+ asks the user once)
    // ─────────────────────────────────────────────────────────
    public static async Task<bool> EnsureReadyAsync()
    {
        try
        {
            if (await LocalNotificationCenter.Current.AreNotificationsEnabled())
                return true;

            return await LocalNotificationCenter.Current.RequestNotificationPermission();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemNotifier] permission: {ex}");
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  SHOW NOW
    // ─────────────────────────────────────────────────────────
    public static Task ShowNowAsync(
        AppNotificationKind kind, int id, string title, string message, string? route = null)
        => ShowAsync(Build(MakeId(kind, id, 0), title, message, kind, route, when: null));

    // ─────────────────────────────────────────────────────────
    //  SCHEDULE (works with the app closed)
    // ─────────────────────────────────────────────────────────
    public static Task ScheduleAsync(
        AppNotificationKind kind, int id, int slot,
        string title, string message, DateTime whenLocal, string? route = null)
        => ShowAsync(Build(MakeId(kind, id, slot), title, message, kind, route, new DateTimeOffset(whenLocal)));

    /// <summary>
    /// For NotificationService: schedules with the DB row's own Id, so the existing
    /// LocalNotificationCenter.Current.Cancel(id) calls keep working.
    /// The channel is picked from the route (BillListPage -> Bills, EmiListPage -> EMI ...).
    /// </summary>
    public static Task ScheduleWithIdAsync(
        int notificationId, string? title, string? message, DateTime reminderDate, string? route)
    {
        var when = reminderDate.Kind == DateTimeKind.Utc ? reminderDate.ToLocalTime() : reminderDate;

        return ShowAsync(Build(
            notificationId,
            string.IsNullOrWhiteSpace(title) ? "MoneySpend" : title,
            message ?? string.Empty,
            KindFromRoute(route),
            route,
            new DateTimeOffset(when)));
    }

    private static AppNotificationKind KindFromRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return AppNotificationKind.General;

        if (route.Contains("Bill", StringComparison.OrdinalIgnoreCase)) return AppNotificationKind.Bill;
        if (route.Contains("Emi", StringComparison.OrdinalIgnoreCase)) return AppNotificationKind.Emi;
        if (route.Contains("Budget", StringComparison.OrdinalIgnoreCase)) return AppNotificationKind.Budget;
        if (route.Contains("Goal", StringComparison.OrdinalIgnoreCase)) return AppNotificationKind.Goal;

        if (route.Contains("BorrowLend", StringComparison.OrdinalIgnoreCase) ||
            route.Contains("Contact", StringComparison.OrdinalIgnoreCase) ||
            route.Contains("Split", StringComparison.OrdinalIgnoreCase) ||
            route.Contains("Detected", StringComparison.OrdinalIgnoreCase))
            return AppNotificationKind.Payment;

        return AppNotificationKind.General;
    }

    /// <summary>
    /// Bill / EMI / any due date: one reminder the day before and one on the due day (9:00 AM).
    /// Calling it again for the same item replaces the old reminders (safe to call on every save).
    /// </summary>
    public static async Task ScheduleDueReminderAsync(
        AppNotificationKind kind, int entityId, string name, decimal amount, DateTime dueDate, string? route = null)
    {
        Cancel(kind, entityId);

        var label = kind switch
        {
            AppNotificationKind.Bill => "Bill",
            AppNotificationKind.Emi => "EMI",
            _ => "Payment"
        };

        var due = dueDate.Date;
        var money = $"₹{amount:N0}";
        var now = DateTime.Now;

        var dayBefore = due.AddDays(-1).AddHours(ReminderHour);
        var onTheDay = due.AddHours(ReminderHour);

        if (dayBefore > now)
        {
            await ScheduleAsync(kind, entityId, 1,
                $"{label} due tomorrow",
                $"{name} • {money} is due tomorrow ({due:dd MMM}).",
                dayBefore, route);
        }

        if (onTheDay > now)
        {
            await ScheduleAsync(kind, entityId, 2,
                $"{label} due today",
                $"{name} • {money} is due today. Tap to pay.",
                onTheDay, route);
        }
    }

    /// <summary>Remove the pending reminders of one item (paid / deleted / edited).</summary>
    public static void Cancel(AppNotificationKind kind, int entityId)
    {
        try
        {
            LocalNotificationCenter.Current.Cancel(
                MakeId(kind, entityId, 0),
                MakeId(kind, entityId, 1),
                MakeId(kind, entityId, 2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemNotifier] cancel: {ex}");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  INTERNALS
    // ─────────────────────────────────────────────────────────
    private static async Task ShowAsync(NotificationRequest request)
    {
        try
        {
            await LocalNotificationCenter.Current.Show(request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemNotifier] show: {ex}");
        }
    }

    private static NotificationRequest Build(
        int id, string title, string message, AppNotificationKind kind, string? route, DateTimeOffset? when)
    {
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = message,                 // long text expands automatically (BigText)
            ReturningData = route ?? string.Empty, // read back when the user taps it
            Android =
            {
                ChannelId = ChannelFor(kind),
                Priority = AndroidPriority.High,   // heads-up banner
                Color = new AndroidColor(AccentArgb)
            }
        };

        request.Android.IconSmallName.ResourceName = SmallIcon;

        if (when is not null)
        {
            request.Schedule.NotifyTime = when;
            request.Schedule.RepeatType = NotificationRepeat.No;

            // fires even in Doze mode and needs NO special "alarm" permission
            request.Schedule.Android.ScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle;
        }

        return request;
    }

    private static string ChannelFor(AppNotificationKind kind) => kind switch
    {
        AppNotificationKind.Bill => BillsChannel,
        AppNotificationKind.Emi => EmiChannel,
        AppNotificationKind.Budget => BudgetChannel,
        AppNotificationKind.Payment => PaymentsChannel,
        _ => GeneralChannel
    };

    /// <summary>Unique int per (kind, item, slot): 0 = instant, 1 = day before, 2 = on the day.</summary>
    private static int MakeId(AppNotificationKind kind, int entityId, int slot)
        => ((int)kind * 1_000_000) + (Math.Abs(entityId) % 99_999 * 10) + slot;
}

/// <summary>Opens the right page when the user taps a notification.</summary>
public static class NotificationTapRouter
{
    public static void Register()
    {
        LocalNotificationCenter.Current.NotificationActionTapped += OnTapped;
    }

    private static void OnTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
    {
        if (!e.IsTapped) return;

        var route = e.Request?.ReturningData;
        if (string.IsNullOrWhiteSpace(route)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync(route);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationTapRouter] {ex}");
            }
        });
    }
}