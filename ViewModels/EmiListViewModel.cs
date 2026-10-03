using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class EmiListViewModel : BaseViewModel
{
    private readonly IEmiService _emiService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<EmiMaster> Emis { get; } = new();

    public EmiListViewModel(IEmiService emiService)
    {
        _emiService = emiService;
        Title = "EMIs";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // CHANGED: asli load logic yahan (silent, spinner nahi)
    private async Task ReloadAsync()
    {
        var emis = await _emiService.GetAllAsync(includeCompleted: true);
        System.Diagnostics.Debug.WriteLine($"[EMI DEBUG] Loaded {emis.Count} EMIs from service");
        Emis.Clear();
        foreach (var e in emis)
            Emis.Add(e);
        System.Diagnostics.Debug.WriteLine($"[EMI DEBUG] Emis collection now has {Emis.Count} items");
    }

    // CHANGED: command ab ReloadAsync use karta hai, pehli load ke baad auto-refresh on
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
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
            Emis.Remove(emi); // turant gayab ho; baaki refresh auto-publish se
        });
    }

    [RelayCommand]
    private static async Task GoToDetailAsync(EmiMaster emi)
    {
        var route = nameof(EmiDetailViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?EmiId={emi.Id}");
    }
}