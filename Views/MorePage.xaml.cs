namespace Arthiva.Views;

public partial class MorePage : ContentPage
{
    public MorePage()
    {
        InitializeComponent();
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(UserProfilePage));

    private async void OnBillsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BillListPage));

    private async void OnEmiTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(EmiListPage));

    private async void OnBorrowLendTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BorrowLendListPage));

    private async void OnContactsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(ContactListPage));

    private async void OnSavingGoalsTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(SavingGoalListPage));

    private async void OnBudgetTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BudgetListPage));

    private async void OnCategoriesTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(CategoryListPage));

    private async void OnComingSoonTapped(object? sender, TappedEventArgs e)
        => await DisplayAlert("Coming Soon", "This feature is under development.", "OK");
}
