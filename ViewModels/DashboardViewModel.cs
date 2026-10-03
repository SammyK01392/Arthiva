using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IAccountService _accountService;
    private readonly ITransactionService _transactionService;
    private readonly IBillService _billService;
    private readonly IEmiService _emiService;
    private readonly IBorrowLendService _borrowLendService;
    private readonly IUserProfileService _userProfileService;
    private readonly AutoRefresh _autoRefresh;

    [ObservableProperty] private string userName = "there";
    [ObservableProperty] private decimal totalBalance;
    [ObservableProperty] private decimal totalReceivable;
    [ObservableProperty] private decimal totalPayable;
    [ObservableProperty] private string viewMode = "Month";
    [ObservableProperty] private DateTime selectedPeriod = DateTime.Today;
    [ObservableProperty] private string periodLabel = string.Empty;
    [ObservableProperty] private decimal periodIncome;
    [ObservableProperty] private decimal periodExpense;
    [ObservableProperty] private decimal periodSaving;
    [ObservableProperty] private int periodIncomeCount;
    [ObservableProperty] private int periodExpenseCount;
    [ObservableProperty] private double spendRatio;
    [ObservableProperty] private string spendPercentText = "0%";
    [ObservableProperty] private string spendInsight = "No transactions in this period yet.";

    public decimal NetBorrowLend => TotalReceivable - TotalPayable;

    // NEW
    public string TodayText => DateTime.Now.ToString("dddd, dd MMM");

    // NEW
    public string UserInitial =>
        string.IsNullOrWhiteSpace(UserName) ? "?" : UserName.Trim()[0].ToString().ToUpper();

    // NEW
    partial void OnUserNameChanged(string value) => OnPropertyChanged(nameof(UserInitial));

    public string GreetingPrefix
    {
        get
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 12 => "Good Morning",
                < 17 => "Good Afternoon",
                _ => "Good Evening"
            };
        }
    }

    public ObservableCollection<Transaction> RecentTransactions { get; } = new();
    public ObservableCollection<Bill> UpcomingBills { get; } = new();
    public ObservableCollection<EmiMaster> ActiveEmis { get; } = new();

    public DashboardViewModel(
        IAccountService accountService,
        ITransactionService transactionService,
        IBillService billService,
        IEmiService emiService,
        IBorrowLendService borrowLendService,
        IUserProfileService userProfileService)
    {
        _accountService = accountService;
        _transactionService = transactionService;
        _billService = billService;
        _emiService = emiService;
        _borrowLendService = borrowLendService;
        _userProfileService = userProfileService;
        Title = "Dashboard";
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private async Task ReloadAsync()
    {
        var profile = await _userProfileService.GetProfileAsync();
        UserName = string.IsNullOrWhiteSpace(profile?.FullName) ? "there" : profile.FullName.Split(' ')[0];

        TotalBalance = await _accountService.GetTotalBalanceAsync();

        TotalReceivable = await _borrowLendService.GetTotalReceivableAsync();
        TotalPayable = await _borrowLendService.GetTotalPayableAsync();
        OnPropertyChanged(nameof(NetBorrowLend));

        await RefreshPeriodTotalsAsync();

        var recent = await _transactionService.GetAllAsync();
        var upcoming = await _billService.GetUpcomingAsync(7);
        var emis = await _emiService.GetAllAsync();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            RecentTransactions.Clear();
            foreach (var t in recent.Take(10))
                RecentTransactions.Add(t);

            UpcomingBills.Clear();
            foreach (var b in upcoming)
                UpcomingBills.Add(b);

            ActiveEmis.Clear();
            foreach (var e in emis)
                ActiveEmis.Add(e);
        });
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private Task SetViewModeAsync(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || ViewMode == mode)
            return Task.CompletedTask;

        ViewMode = mode;
        SelectedPeriod = DateTime.Today;
        return RefreshPeriodTotalsAsync();
    }

    [RelayCommand]
    private Task PreviousPeriodAsync()
    {
        SelectedPeriod = ShiftPeriod(SelectedPeriod, -1);
        return RefreshPeriodTotalsAsync();
    }

    [RelayCommand]
    private Task NextPeriodAsync()
    {
        SelectedPeriod = ShiftPeriod(SelectedPeriod, 1);
        return RefreshPeriodTotalsAsync();
    }

    private DateTime ShiftPeriod(DateTime date, int direction) => ViewMode switch
    {
        "Day" => date.AddDays(direction),
        "Week" => date.AddDays(7 * direction),
        "Year" => date.AddYears(direction),
        _ => date.AddMonths(direction)
    };

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.Date.AddDays(-diff);
    }

    private async Task RefreshPeriodTotalsAsync()
    {
        DateTime from, to;
        var today = DateTime.Today;
        var culture = CultureInfo.CurrentCulture;

        switch (ViewMode)
        {
            case "Day":
                from = SelectedPeriod.Date;
                to = from.AddDays(1).AddTicks(-1);
                PeriodLabel = from == today ? "Today"
                    : from == today.AddDays(-1) ? "Yesterday"
                    : from == today.AddDays(1) ? "Tomorrow"
                    : from.ToString("dd MMM yyyy", culture);
                break;

            case "Week":
                from = StartOfWeek(SelectedPeriod);
                to = from.AddDays(7).AddTicks(-1);
                if (from == StartOfWeek(today))
                {
                    PeriodLabel = "This Week";
                }
                else
                {
                    var end = from.AddDays(6);
                    PeriodLabel = $"{from.ToString("dd MMM", culture)} - {end.ToString("dd MMM", culture)}";
                }
                break;

            case "Year":
                from = new DateTime(SelectedPeriod.Year, 1, 1);
                to = from.AddYears(1).AddTicks(-1);
                PeriodLabel = SelectedPeriod.Year == today.Year
                    ? "This Year"
                    : SelectedPeriod.Year.ToString();
                break;

            default:
                from = new DateTime(SelectedPeriod.Year, SelectedPeriod.Month, 1);
                to = from.AddMonths(1).AddTicks(-1);
                PeriodLabel = SelectedPeriod.Year == today.Year && SelectedPeriod.Month == today.Month
                    ? "This Month"
                    : SelectedPeriod.ToString("MMM yyyy", culture);
                break;
        }

        var periodTransactions = await _transactionService.GetByDateRangeAsync(from, to);

        var income = periodTransactions.Where(t => t.TransactionType == "Income").ToList();
        var expense = periodTransactions.Where(t => t.TransactionType == "Expense").ToList();

        PeriodIncome = income.Sum(t => t.Amount);
        PeriodExpense = expense.Sum(t => t.Amount);
        PeriodSaving = PeriodIncome - PeriodExpense;
        PeriodIncomeCount = income.Count;
        PeriodExpenseCount = expense.Count;

        UpdateSpendInsight();
    }

    private void UpdateSpendInsight()
    {
        if (PeriodIncome > 0)
        {
            var ratio = (double)(PeriodExpense / PeriodIncome);
            SpendRatio = Math.Min(ratio, 1);
            SpendPercentText = $"{ratio * 100:0}%";
            SpendInsight = ratio switch
            {
                <= 0.5 => "Great! You're spending less than half of your income.",
                <= 0.8 => "Doing fine. Keep an eye on your spending.",
                <= 1.0 => "You're close to your income limit.",
                _ => "You've spent more than you earned in this period."
            };
        }
        else if (PeriodExpense > 0)
        {
            SpendRatio = 1;
            SpendPercentText = "100%+";
            SpendInsight = "You have expenses but no income in this period.";
        }
        else
        {
            SpendRatio = 0;
            SpendPercentText = "0%";
            SpendInsight = "No transactions in this period yet.";
        }
    }
}