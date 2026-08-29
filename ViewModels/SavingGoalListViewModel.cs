using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class SavingGoalListViewModel : BaseViewModel
{
    private readonly ISavingGoalService _savingGoalService;

    public ObservableCollection<SavingGoal> Goals { get; } = new();

    public SavingGoalListViewModel(ISavingGoalService savingGoalService)
    {
        _savingGoalService = savingGoalService;
        Title = "Saving Goals";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var goals = await _savingGoalService.GetAllAsync(includeCompleted: true);
            Goals.Clear();
            foreach (var g in goals)
                Goals.Add(g);
        });
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(SavingGoalEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(SavingGoal goal)
    {
        var route = nameof(SavingGoalEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?GoalId={goal.Id}");
    }

    [RelayCommand]
    private static async Task GoToContributeAsync(SavingGoal goal)
    {
        var route = nameof(SavingGoalContributeViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?GoalId={goal.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(SavingGoal goal)
    {
        await ExecuteAsync(async () =>
        {
            await _savingGoalService.SoftDeleteAsync(goal.Id);
            Goals.Remove(goal);
        });
    }
}
