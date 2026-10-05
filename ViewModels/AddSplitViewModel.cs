using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>One person in the split (the first row is always "You").</summary>
public partial class ParticipantRow : ObservableObject
{
    /// <summary>null = you.</summary>
    public int? ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsMe => ContactId is null;

    [ObservableProperty] private bool isSelected;

    /// <summary>Typed value: amount (Exact) or percent (Percent). Unused for Equal.</summary>
    [ObservableProperty] private string input = string.Empty;

    /// <summary>Computed ₹ share for this person.</summary>
    [ObservableProperty] private decimal share;

    [ObservableProperty] private bool showInput;

    [ObservableProperty] private string inputHint = "₹";
}

public partial class AddSplitViewModel : BaseViewModel
{
    private readonly ISplitService _splitService;
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;

    private readonly List<ParticipantRow> _allRows = new();
    private bool _loaded;
    private bool _suspend;

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<ParticipantRow> VisibleRows { get; } = new();

    [ObservableProperty] private string expenseTitle = string.Empty;
    [ObservableProperty] private string totalText = string.Empty;
    [ObservableProperty] private DateTime splitDate = DateTime.Today;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private Account? selectedAccount;
    [ObservableProperty] private Category? selectedCategory;

    [ObservableProperty] private string selectedMethod = "Equal"; // Equal / Exact / Percent
    [ObservableProperty] private string contactSearch = string.Empty;

    [ObservableProperty] private string summaryText = "Enter the amount and pick who is in";
    [ObservableProperty] private bool isSplitValid;

    partial void OnTotalTextChanged(string value) => Recalculate();

    partial void OnSelectedMethodChanged(string value)
    {
        PrefillInputs();
        Recalculate();
    }

    partial void OnContactSearchChanged(string value) => ApplySearch();

    public AddSplitViewModel(
        ISplitService splitService,
        IAccountService accountService,
        ICategoryService categoryService)
    {
        _splitService = splitService;
        _accountService = accountService;
        _categoryService = categoryService;
        Title = "Split Expense";
    }

    // ─────────────────────────────────────────────
    //  Load
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loaded) return;

        await ExecuteAsync(async () =>
        {
            // NOTE: assumes IAccountService / ICategoryService expose GetAllAsync().
            // If your method names differ, change just these two lines.
            var accounts = await _accountService.GetAllAsync();
            var categories = await _categoryService.GetAllAsync();
            var contacts = await _splitService.GetContactsAsync();

            Accounts.Clear();
            foreach (var a in accounts) Accounts.Add(a);
            SelectedAccount ??= Accounts.FirstOrDefault();

            Categories.Clear();
            foreach (var c in categories) Categories.Add(c);
            SelectedCategory ??= Categories.FirstOrDefault();

            _allRows.Clear();
            Track(new ParticipantRow { Name = "You", IsSelected = true });
            foreach (var contact in contacts)
                Track(new ParticipantRow { ContactId = contact.Id, Name = contact.Name });

            ApplySearch();
            Recalculate();
            _loaded = true;
        });
    }

    private ParticipantRow Track(ParticipantRow row)
    {
        row.PropertyChanged += OnRowChanged;
        _allRows.Add(row);
        return row;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspend) return;

        if (e.PropertyName is nameof(ParticipantRow.IsSelected) or nameof(ParticipantRow.Input))
            Recalculate();
    }

    // ─────────────────────────────────────────────
    //  Search (selected people always stay visible)
    // ─────────────────────────────────────────────
    private void ApplySearch()
    {
        var q = ContactSearch?.Trim() ?? string.Empty;

        var rows = _allRows.Where(r =>
            r.IsMe || r.IsSelected || q.Length == 0 ||
            r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        VisibleRows.Clear();
        foreach (var r in rows) VisibleRows.Add(r);
    }

    // ─────────────────────────────────────────────
    //  Split maths
    // ─────────────────────────────────────────────
    [RelayCommand]
    private void SetMethod(string method) => SelectedMethod = method;

    /// <summary>When switching to Exact/Percent, start from an equal split so the user only tweaks.</summary>
    private void PrefillInputs()
    {
        var selected = _allRows.Where(r => r.IsSelected).ToList();

        _suspend = true;
        try
        {
            if (SelectedMethod == "Exact")
            {
                var parts = SplitCalculator.Equal(SplitCalculator.ParseAmount(TotalText), selected.Count);
                for (var i = 0; i < selected.Count; i++)
                    selected[i].Input = parts[i] == 0m ? string.Empty : parts[i].ToString("0.##", CultureInfo.CurrentCulture);
            }
            else if (SelectedMethod == "Percent")
            {
                var parts = SplitCalculator.Equal(100m, selected.Count);
                for (var i = 0; i < selected.Count; i++)
                    selected[i].Input = parts[i].ToString("0.##", CultureInfo.CurrentCulture);
            }
            else
            {
                foreach (var r in _allRows) r.Input = string.Empty;
            }
        }
        finally
        {
            _suspend = false;
        }
    }

    private void Recalculate()
    {
        if (_allRows.Count == 0) return;

        var total = SplitCalculator.ParseAmount(TotalText);
        var selected = _allRows.Where(r => r.IsSelected).ToList();

        _suspend = true;
        try
        {
            foreach (var r in _allRows)
            {
                r.ShowInput = SelectedMethod != "Equal" && r.IsSelected;
                r.InputHint = SelectedMethod == "Percent" ? "%" : "₹";
                if (!r.IsSelected) r.Share = 0m;
            }

            switch (SelectedMethod)
            {
                case "Exact":
                {
                    foreach (var r in selected) r.Share = SplitCalculator.ParseAmount(r.Input);

                    var diff = total - selected.Sum(r => r.Share);
                    IsSplitValid = total > 0 && selected.Count > 0 && diff == 0m;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : diff == 0m ? "All assigned"
                        : diff > 0 ? $"₹{diff:N2} left to assign"
                        : $"₹{-diff:N2} over the total";
                    break;
                }

                case "Percent":
                {
                    var percents = selected.Select(r => SplitCalculator.ParseAmount(r.Input)).ToList();
                    var amounts = SplitCalculator.Percent(total, percents);
                    for (var i = 0; i < selected.Count; i++) selected[i].Share = amounts[i];

                    var sumPercent = percents.Sum();
                    IsSplitValid = total > 0 && selected.Count > 0 && sumPercent == 100m;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : sumPercent == 100m ? "100% assigned"
                        : $"{sumPercent:0.##}% of 100% assigned";
                    break;
                }

                default: // Equal
                {
                    var parts = SplitCalculator.Equal(total, selected.Count);
                    for (var i = 0; i < selected.Count; i++) selected[i].Share = parts[i];

                    IsSplitValid = total > 0 && selected.Count > 0;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : selected.Count == 0 ? "Pick who is in this split"
                        : $"₹{SplitCalculator.Money(total)} ÷ {selected.Count} people";
                    break;
                }
            }
        }
        finally
        {
            _suspend = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        var total = SplitCalculator.ParseAmount(TotalText);

        if (string.IsNullOrWhiteSpace(ExpenseTitle)) { await AlertAsync("Enter what this expense was for."); return; }
        if (total <= 0) { await AlertAsync("Enter a valid total amount."); return; }
        if (SelectedAccount is null) { await AlertAsync("Select the account you paid from."); return; }

        var me = _allRows.First(r => r.IsMe);
        var myShare = me.IsSelected ? me.Share : 0m;

        if (myShare > 0 && SelectedCategory is null) { await AlertAsync("Select a category for your share."); return; }

        var friends = _allRows.Where(r => r.IsSelected && !r.IsMe && r.Share > 0).ToList();
        if (friends.Count == 0) { await AlertAsync("Pick at least one friend with a share above zero."); return; }

        if (!IsSplitValid) { await AlertAsync(SummaryText); return; }

        var request = new SplitRequest(
            ExpenseTitle.Trim(),
            total,
            SelectedMethod,
            SplitDate.Date + DateTime.Now.TimeOfDay,
            SelectedAccount.Id,
            SelectedCategory?.Id ?? 0,
            myShare,
            friends.Select(f => new SplitShareInput(f.ContactId!.Value, f.Share)).ToList(),
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.CreateAsync(request));

        if (result is null) return;

        if (result.Success)
            await Shell.Current.GoToAsync("..");
        else
            await AlertAsync(result.ErrorMessage ?? "Could not save the split.");
    }

    private static Task AlertAsync(string message)
        => Shell.Current.DisplayAlert("Split", message, "OK");
}
