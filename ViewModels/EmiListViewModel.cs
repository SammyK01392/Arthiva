using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class EmiListViewModel : BaseViewModel
{
    private readonly IEmiService _emiService;

    public ObservableCollection<EmiMaster> Emis { get; } = new();

    public EmiListViewModel(IEmiService emiService)
    {
        _emiService = emiService;
        Title = "EMIs";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var emis = await _emiService.GetAllAsync(includeCompleted: true);
            Emis.Clear();
            foreach (var e in emis)
                Emis.Add(e);
        });
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(EmiEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(EmiMaster emi)
    {
        var route = nameof(EmiEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?EmiId={emi.Id}");
    }

    [RelayCommand]
    private static async Task GoToPayAsync(EmiMaster emi)
    {
        var route = nameof(RecordEmiPaymentViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?EmiId={emi.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(EmiMaster emi)
    {
        await ExecuteAsync(async () =>
        {
            await _emiService.SoftDeleteAsync(emi.Id);
            Emis.Remove(emi);
        });
    }
}
