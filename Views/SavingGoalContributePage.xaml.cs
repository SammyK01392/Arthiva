using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class SavingGoalContributePage : ContentPage
{
    private readonly SavingGoalContributeViewModel _viewModel;

    public SavingGoalContributePage(SavingGoalContributeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.AppearingCommand.Execute(null);
    }
}
