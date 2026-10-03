using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class BillListViewModel : BaseViewModel
{
    private readonly IBillService _billService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    private List<Bill> _allBills = new();

    public ObservableCollection<Bill> Bills { get; } = new();

    [ObservableProperty]
    private string selectedFilter = "All"; // All / Upcoming / Overdue

    public List<string> FilterOptions { get; } = new() { "All", "Upcoming", "Overdue" };

    // CHANGED: filter badalne par guard-free, serialized refresh
    partial void OnSelectedFilterChanged(string value)
        => _autoRefresh.Request();

    public BillListViewModel(IBillService billService)
    {
        _billService = billService;
        Title = "Bills";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // CHANGED: asli load logic yahan (silent, spinner nahi)
    private async Task ReloadAsync()
    {
        _allBills = SelectedFilter switch
        {
            "Upcoming" => await _billService.GetUpcomingAsync(7),
            "Overdue" => await _billService.GetOverdueAsync(),
            _ => await _billService.GetAllAsync()
        };

        Bills.Clear();
        foreach (var b in _allBills)
            Bills.Add(b);
    }

    // CHANGED: command ab ReloadAsync use karta hai, pehli load ke baad auto-refresh on
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private void SetFilter(string filter)
        => SelectedFilter = filter;

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(BillEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(Bill bill)
    {
        var route = nameof(BillEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?BillId={bill.Id}");
    }

    [RelayCommand]
    private static async Task GoToPayAsync(Bill bill)
    {
        var route = nameof(RecordBillPaymentViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?BillId={bill.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(Bill bill)
    {
        await ExecuteAsync(async () =>
        {
            await _billService.SoftDeleteAsync(bill.Id);
            Bills.Remove(bill); // turant gayab ho; baaki refresh auto-publish se
        });
    }
}