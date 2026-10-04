using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class UserProfilePage : ContentPage
{
    private readonly UserProfileViewModel _viewModel;

    public UserProfilePage(UserProfileViewModel viewModel)
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

    private async void OnAvatarTapped(object? sender, TappedEventArgs e)
    {
        var options = new List<string> { "Choose from gallery", "Take photo" };
        if (_viewModel.HasProfileImage)
            options.Add("Remove photo");

        var choice = await DisplayActionSheet(
            "Profile photo", "Cancel", null, options.ToArray());

        switch (choice)
        {
            case "Choose from gallery":
                await _viewModel.PickFromGalleryCommand.ExecuteAsync(null);
                break;
            case "Take photo":
                await _viewModel.TakePhotoCommand.ExecuteAsync(null);
                break;
            case "Remove photo":
                _viewModel.RemoveImageCommand.Execute(null);
                break;
        }
    }
}