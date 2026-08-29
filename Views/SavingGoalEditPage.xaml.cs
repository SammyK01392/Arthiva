using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class SavingGoalEditPage : ContentPage
{
    private readonly SavingGoalEditViewModel _viewModel;

    public SavingGoalEditPage(SavingGoalEditViewModel viewModel)
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
