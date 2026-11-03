using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class DetectedTransactionsViewModel : BaseViewModel
{
    private readonly IDetectedTransactionService _service;
    private readonly IGenericRepository<Category> _categoryRepo;

    public ObservableCollection<DetectedTransaction> Items { get; } = new();

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool needsPermission;

    /// <summary>ON = every detected SMS becomes a real entry immediately.</summary>
    [ObservableProperty]
    private bool autoAdd;

    public DetectedTransactionsViewModel(
        IDetectedTransactionService service,
        IGenericRepository<Category> categoryRepo)
    {
        _service = service;
        _categoryRepo = categoryRepo;
        Title = "Detected Payments";

        autoAdd = SmsAutoDetectSettings.AutoAdd;   // field: does not trigger the change handler
    }

    /// <summary>False in the normal build (no SMS permission, installs from anywhere).</summary>
    public bool SmsFeatureEnabled
    {
        get
        {
#if SMS_DETECT
            return true;
#else
            return false;
#endif
        }
    }

    public bool SmsFeatureDisabled => !SmsFeatureEnabled;

    partial void OnAutoAddChanged(bool value)
        => SmsAutoDetectSettings.AutoAdd = value;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
#if ANDROID && SMS_DETECT
            var status = await Permissions.CheckStatusAsync<SmsReadPermission>();
            NeedsPermission = status != PermissionStatus.Granted;
#endif
            var pending = await _service.GetPendingAsync();

            Items.Clear();
            foreach (var p in pending)
                Items.Add(p);

            IsEmpty = Items.Count == 0;
        });
    }

    /// <summary>Known payee -> adds straight away. New payee -> asks for the category once.</summary>
    [RelayCommand]
    private async Task AddToExpensesAsync(DetectedTransaction item)
    {
        var categories = await _categoryRepo.FindAsync(c => true);

        if (categories.Count == 0)
        {
            await Shell.Current.DisplayAlert("No categories", "Please create a category first.", "OK");
            return;
        }

        int categoryId;

        var remembered = item.SuggestedCategoryId is int id
            ? categories.FirstOrDefault(c => c.Id == id)
            : null;

        if (remembered is not null)
        {
            categoryId = remembered.Id;
        }
        else
        {
            var names = categories.Select(c => c.Name).Distinct().ToArray();

            var choice = await Shell.Current.DisplayActionSheet(
                $"{(item.IsCredit ? "+" : "-")}₹{item.Amount:N2} • {item.DisplayPayee}",
                "Cancel",
                null,
                names);

            if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

            categoryId = categories.First(c => c.Name == choice).Id;
        }

        await ExecuteAsync(async () =>
        {
            await _service.AddAsTransactionAsync(item, categoryId);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        });
    }

    [RelayCommand]
    private async Task SkipAsync(DetectedTransaction item)
    {
        await ExecuteAsync(async () =>
        {
            await _service.DismissAsync(item);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        });
    }

    /// <summary>Tap the name -> rename (e.g. "paytmqr281@paytm" -> "Sharma Chai Stall"). Remembered for next time.</summary>
    [RelayCommand]
    private async Task RenamePayeeAsync(DetectedTransaction item)
    {
        var name = await Shell.Current.DisplayPromptAsync(
            "Payee name",
            "Entries will be saved with this name, and it is remembered for the next payment.",
            "Save",
            "Cancel",
            maxLength: 100,
            initialValue: item.Payee ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name)) return;

        await _service.RenamePayeeAsync(item, name);

        // refresh the card
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items.RemoveAt(index);
            Items.Insert(index, item);
        }
    }

    [RelayCommand]
    private async Task EnableAutoDetectAsync()
    {
#if ANDROID && SMS_DETECT
        var status = await Permissions.RequestAsync<SmsReadPermission>();
        NeedsPermission = status != PermissionStatus.Granted;

        if (NeedsPermission)
        {
            await Shell.Current.DisplayAlert(
                "Permission needed",
                "Allow SMS permission from Settings > Apps > MoneySpend > Permissions.",
                "OK");
        }
#else
        await Task.CompletedTask;
#endif
    }
}
