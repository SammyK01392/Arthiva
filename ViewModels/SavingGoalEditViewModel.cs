using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(GoalId), "GoalId")]
public partial class SavingGoalEditViewModel : BaseViewModel
{
    private readonly ISavingGoalService _savingGoalService;

    private int _goalId;
    private SavingGoal? _existingGoal;

    public string GoalId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _goalId = id;
        }
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private decimal targetAmount;

    [ObservableProperty]
    private DateTime? targetDate;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string? icon;

    [ObservableProperty]
    private string? colorCode;

    public SavingGoalEditViewModel(ISavingGoalService savingGoalService)
    {
        _savingGoalService = savingGoalService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        if (_goalId <= 0)
        {
            Title = "Add Saving Goal";
            IsEditMode = false;
            return;
        }

        await ExecuteAsync(async () =>
        {
            _existingGoal = await _savingGoalService.GetByIdAsync(_goalId);
            if (_existingGoal is null) return;

            Title = "Edit Saving Goal";
            IsEditMode = true;
            Name = _existingGoal.Name;
            TargetAmount = _existingGoal.TargetAmount;
            TargetDate = _existingGoal.TargetDate;
            Description = _existingGoal.Description;
            Icon = _existingGoal.Icon;
            ColorCode = _existingGoal.ColorCode;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || TargetAmount <= 0)
        {
            ErrorMessage = "Enter a goal name and a valid target amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingGoal is not null)
            {
                _existingGoal.Name = Name;
                _existingGoal.TargetAmount = TargetAmount;
                _existingGoal.TargetDate = TargetDate;
                _existingGoal.Description = Description;
                _existingGoal.Icon = Icon;
                _existingGoal.ColorCode = ColorCode;

                await _savingGoalService.UpdateAsync(_existingGoal);
            }
            else
            {
                var goal = new SavingGoal
                {
                    Name = Name,
                    TargetAmount = TargetAmount,
                    TargetDate = TargetDate,
                    Description = Description,
                    Icon = Icon,
                    ColorCode = ColorCode
                };
                await _savingGoalService.CreateAsync(goal);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
