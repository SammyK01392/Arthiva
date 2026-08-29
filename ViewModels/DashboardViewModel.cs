using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private decimal totalBalance;

    [ObservableProperty]
    private decimal monthIncome;

    [ObservableProperty]
    private decimal monthExpense;

    [ObservableProperty]
    private decimal monthSaving;

    public ObservableCollection<Transaction> RecentTransactions { get; } = new();

    public ObservableCollection<Bill> UpcomingBills { get; } = new();

    public ObservableCollection<EmiMaster> ActiveEmis { get; } = new();

    public DashboardViewModel(
        IAccountService accountService,
        ITransactionService transactionService,
        IBillService billService,
        IEmiService emiService)
    {
        _accountService = accountService;
        _transactionService = transactionService;
        _billService = billService;
        _emiService = emiService;
        Title = "Dashboard";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            TotalBalance = await _accountService.GetTotalBalanceAsync();

            var from = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var to = from.AddMonths(1).AddTicks(-1);

            MonthIncome = await _transactionService.GetTotalByTypeAsync("Income", from, to);
            MonthExpense = await _transactionService.GetTotalByTypeAsync("Expense", from, to);
            MonthSaving = MonthIncome - MonthExpense;

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
