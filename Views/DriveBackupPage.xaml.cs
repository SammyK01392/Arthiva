using System;
using System.Collections.Generic;
using System.Text;

using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class DriveBackupPage : ContentPage
{
    private readonly DriveBackupViewModel _vm;
    public DriveBackupPage(DriveBackupViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
