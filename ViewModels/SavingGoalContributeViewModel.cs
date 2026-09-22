using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(GoalId), "GoalId")]
public partial class SavingGoalContributeViewModel : BaseViewModel
{
    private readonly ISavingGoalService _savingGoalService;
    private readonly IAccountService _accountService;

    private int _goalId;

    public string GoalId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _goalId = id;
        }
    }

    [ObservableProperty]
    private SavingGoal? goal;

    [ObservableProperty]
    private string movementType = "Contribution"; // Contribution / Withdrawal

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private DateTime contributionDate = DateTime.Now;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private Account? selectedAccount;

    public ObservableCollection<Account> Accounts { get; } = new();

    public List<string> MovementTypes { get; } = new() { "Contribution", "Withdrawal" };

    public SavingGoalContributeViewModel(ISavingGoalService savingGoalService, IAccountService accountService)
    {
        _savingGoalService = savingGoalService;
        _accountService = accountService;
        Title = "Add / Withdraw";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Goal = await _savingGoalService.GetByIdAsync(_goalId);

            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Goal is null || Amount <= 0)
        {
            ErrorMessage = "Enter a valid amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (MovementType == "Contribution")
                await _savingGoalService.ContributeAsync(Goal.Id, Amount, SelectedAccount?.Id, Notes, ContributionDate);
            else
                await _savingGoalService.WithdrawAsync(Goal.Id, Amount, SelectedAccount?.Id, Notes, ContributionDate);

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}