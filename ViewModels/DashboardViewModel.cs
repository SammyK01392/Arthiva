using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IAccountService _accountService;
    private readonly ITransactionService _transactionService;
    private readonly IBillService _billService;
    private readonly IEmiService _emiService;
    private readonly IBorrowLendService _borrowLendService;
    private readonly IUserProfileService _userProfileService;

    [ObservableProperty]
    private string userName = "there";

    [ObservableProperty]
    private decimal totalBalance;

    [ObservableProperty]
    private decimal totalReceivable;

    [ObservableProperty]
    private decimal totalPayable;

    // ------------------------------------------------------------
    // Month / Year period switcher.
    // Income, Expense and Savings on the dashboard follow whichever
    // period is selected here; Net Balance stays all-time.
    // ------------------------------------------------------------
    [ObservableProperty]
    private DateTime selectedPeriod = DateTime.Today;

    [ObservableProperty]
    private bool isYearView;

    [ObservableProperty]
    private string periodLabel = string.Empty;

    [ObservableProperty]
    private decimal periodIncome;

    [ObservableProperty]
    private decimal periodExpense;

    [ObservableProperty]
    private decimal periodSaving;

    [ObservableProperty]
    private int periodIncomeCount;

    [ObservableProperty]
    private int periodExpenseCount;

    public string ViewModeLabel => IsYearView ? "Yearly" : "Monthly";

    public decimal NetBorrowLend => TotalReceivable - TotalPayable;

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
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var profile = await _userProfileService.GetProfileAsync();
            UserName = string.IsNullOrWhiteSpace(profile?.FullName) ? "there" : profile.FullName.Split(' ')[0];

            TotalBalance = await _accountService.GetTotalBalanceAsync();

            TotalReceivable = await _borrowLendService.GetTotalReceivableAsync();
            TotalPayable = await _borrowLendService.GetTotalPayableAsync();
            OnPropertyChanged(nameof(NetBorrowLend));

            // Month/year income, expense and savings for the currently
            // selected period (defaults to "this month" on first load).
            await RefreshPeriodTotalsAsync();

            var recent = await _transactionService.GetAllAsync();
            var upcoming = await _billService.GetUpcomingAsync(7);
            var emis = await _emiService.GetAllAsync();

            // All ObservableCollection mutations MUST happen on the main
            // thread. On Android, updating them from a background
            // continuation can leave the collection changed but the
            // bound CollectionView/BindableLayout never refreshes visibly
            // — which is why bills/EMIs/transactions can silently fail
            // to appear even though the data loaded correctly.
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
        });
    }

    [RelayCommand]
    private Task PreviousPeriodAsync()
    {
        SelectedPeriod = IsYearView ? SelectedPeriod.AddYears(-1) : SelectedPeriod.AddMonths(-1);
        return RefreshPeriodTotalsAsync();
    }

    [RelayCommand]
    private Task NextPeriodAsync()
    {
        SelectedPeriod = IsYearView ? SelectedPeriod.AddYears(1) : SelectedPeriod.AddMonths(1);
        return RefreshPeriodTotalsAsync();
    }

    [RelayCommand]
    private Task ToggleViewModeAsync()
    {
        IsYearView = !IsYearView;
        OnPropertyChanged(nameof(ViewModeLabel));
        return RefreshPeriodTotalsAsync();
    }

    // ------------------------------------------------------------
    // Recomputes Income / Expense / Savings for whichever period is
    // currently selected (a single month, or a whole year), without
    // reloading everything else on the dashboard. This is what
    // "month wise" and "year wise" switching drives.
    // ------------------------------------------------------------
    private async Task RefreshPeriodTotalsAsync()
    {
        DateTime from, to;

        var today = DateTime.Today;

        if (IsYearView)
        {
            from = new DateTime(SelectedPeriod.Year, 1, 1);
            to = from.AddYears(1).AddTicks(-1);
            PeriodLabel = SelectedPeriod.Year == today.Year
                ? "This Year"
                : SelectedPeriod.Year.ToString();
        }
        else
        {
            from = new DateTime(SelectedPeriod.Year, SelectedPeriod.Month, 1);
            to = from.AddMonths(1).AddTicks(-1);
            PeriodLabel = SelectedPeriod.Year == today.Year && SelectedPeriod.Month == today.Month
                ? "This Month"
                : SelectedPeriod.ToString("MMMM yyyy");
        }

        var periodTransactions = await _transactionService.GetByDateRangeAsync(from, to);

        var income = periodTransactions.Where(t => t.TransactionType == "Income").ToList();
        var expense = periodTransactions.Where(t => t.TransactionType == "Expense").ToList();

        PeriodIncome = income.Sum(t => t.Amount);
        PeriodExpense = expense.Sum(t => t.Amount);
        PeriodSaving = PeriodIncome - PeriodExpense;
        PeriodIncomeCount = income.Count;
        PeriodExpenseCount = expense.Count;
    }
}