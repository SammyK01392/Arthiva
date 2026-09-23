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
    private decimal monthIncome;

    [ObservableProperty]
    private decimal monthExpense;

    [ObservableProperty]
    private decimal monthSaving;

    [ObservableProperty]
    private int incomeTransactionCount;

    [ObservableProperty]
    private int expenseTransactionCount;

    [ObservableProperty]
    private decimal totalReceivable;

    [ObservableProperty]
    private decimal totalPayable;

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

            var from = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var to = from.AddMonths(1).AddTicks(-1);

            MonthIncome = await _transactionService.GetTotalByTypeAsync("Income", from, to);
            MonthExpense = await _transactionService.GetTotalByTypeAsync("Expense", from, to);
            MonthSaving = MonthIncome - MonthExpense;

            var monthTransactions = await _transactionService.GetByDateRangeAsync(from, to);
            IncomeTransactionCount = monthTransactions.Count(t => t.TransactionType == "Income");
            ExpenseTransactionCount = monthTransactions.Count(t => t.TransactionType == "Expense");

            TotalReceivable = await _borrowLendService.GetTotalReceivableAsync();
            TotalPayable = await _borrowLendService.GetTotalPayableAsync();
            OnPropertyChanged(nameof(NetBorrowLend));

            var recent = await _transactionService.GetAllAsync();
            RecentTransactions.Clear();
            foreach (var t in recent.Take(10))
                RecentTransactions.Add(t);

            var upcoming = await _billService.GetUpcomingAsync(7);
            UpcomingBills.Clear();
            foreach (var b in upcoming)
                UpcomingBills.Add(b);

            var emis = await _emiService.GetAllAsync();
            ActiveEmis.Clear();
            foreach (var e in emis)
                ActiveEmis.Add(e);
        });
    }

}