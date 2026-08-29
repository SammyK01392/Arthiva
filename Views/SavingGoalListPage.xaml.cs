using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class SavingGoalListPage : ContentPage
{
    private readonly SavingGoalListViewModel _viewModel;

    public SavingGoalListPage(SavingGoalListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
