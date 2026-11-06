using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class DetectedTransactionsViewModel : BaseViewModel
{
    private readonly IDetectedTransactionService _service;
    private readonly IStatementImportService _import;
    private readonly IGenericRepository<Category> _categoryRepo;
    private readonly IAccountService _accounts;
    private readonly IAccountLinkService _accountLinks;
    private readonly IEmiImportService _emiImport;

    public ObservableCollection<DetectedTransaction> Items { get; } = new();

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool needsPermission;

    /// <summary>ON = every detected SMS becomes a real entry immediately.</summary>
    [ObservableProperty]
    private bool autoAdd;

    /// <summary>How many rows "Add all confident" would add.</summary>
    [ObservableProperty]
    private int confidentCount;

    /// <summary>ON = a payee you already added once is added again directly when you import a statement.</summary>
    [ObservableProperty]
    private bool autoAddKnown;

    /// <summary>Where statement payments go, e.g. "Bank Account".</summary>
    [ObservableProperty]
    private string importAccountName = string.Empty;

    public string ImportAccountText => $"No account number in the entry? It goes to: {ImportAccountName} (tap to change)";

    public bool HasConfident => ConfidentCount > 0;

    public string AddAllText => $"Add all confident ({ConfidentCount})";

    public DetectedTransactionsViewModel(
        IDetectedTransactionService service,
        IStatementImportService import,
        IGenericRepository<Category> categoryRepo,
        IAccountService accounts,
        IAccountLinkService accountLinks,
        IEmiImportService emiImport)
    {
        _service = service;
        _import = import;
        _categoryRepo = categoryRepo;
        _accounts = accounts;
        _accountLinks = accountLinks;
        _emiImport = emiImport;
        Title = "Detected Payments";

        autoAdd = SmsAutoDetectSettings.AutoAdd;   // fields: do not trigger the change handlers
        autoAddKnown = StatementImportSettings.AutoAddKnown;
    }

    partial void OnAutoAddKnownChanged(bool value)
        => StatementImportSettings.AutoAddKnown = value;

    partial void OnImportAccountNameChanged(string value)
        => OnPropertyChanged(nameof(ImportAccountText));

    partial void OnAutoAddChanged(bool value)
        => SmsAutoDetectSettings.AutoAdd = value;

    partial void OnConfidentCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasConfident));
        OnPropertyChanged(nameof(AddAllText));
    }

    /// <summary>False in the normal build (no SMS permission, installs from anywhere).</summary>
    public bool SmsFeatureEnabled
    {
        get
        {
#if SMS_DETECT
            return true;
#else
            return false;
#endif
        }
    }

    public bool SmsFeatureDisabled => !SmsFeatureEnabled;

    // ─────────────────────────────────────────────────────────
    //  LOAD
    // ─────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(LoadAsync);
    }

    private async Task LoadAsync()
    {
#if ANDROID && SMS_DETECT
        var status = await Permissions.CheckStatusAsync<SmsReadPermission>();
        NeedsPermission = status != PermissionStatus.Granted;
#endif
        await RefreshImportAccountNameAsync();

        var pending = await _service.GetPendingAsync();

        // needs-review rows first. Statement rows oldest first (so person payments are added in the
        // right order), SMS rows newest first.
        var ordered = pending
            .OrderBy(p => p.IsLow ? 0 : 1)
            .ThenBy(p => p.IsFromStatement ? p.TransactionDate.Ticks : -p.TransactionDate.Ticks)
            .ToList();

        Items.Clear();
        foreach (var p in ordered)
            Items.Add(p);

        RefreshCounts();
    }

    private void RefreshCounts()
    {
        IsEmpty = Items.Count == 0;
        ConfidentCount = Items.Count(i => i.IsAutoAddable);
    }

    // ─────────────────────────────────────────────────────────
    //  WHICH ACCOUNT DO STATEMENT PAYMENTS GO TO?
    // ─────────────────────────────────────────────────────────
    private async Task RefreshImportAccountNameAsync()
    {
        var accounts = await _accounts.GetAllAsync();
        var chosen = accounts.FirstOrDefault(a => a.Id == StatementImportSettings.ImportAccountId);

        ImportAccountName = chosen?.Name
            ?? accounts.FirstOrDefault(a => string.Equals(a.Type, "Bank", StringComparison.OrdinalIgnoreCase))?.Name
            ?? "Default account";
    }

    [RelayCommand]
    private async Task ChooseImportAccountAsync()
    {
        var accounts = await _accounts.GetAllAsync();

        if (accounts.Count == 0)
        {
            await Shell.Current.DisplayAlert("No accounts", "Please create an account first.", "OK");
            return;
        }

        var choice = await Shell.Current.DisplayActionSheet(
            "Statement payments are recorded in...",
            "Cancel",
            null,
            accounts.Select(a => a.Name).ToArray());

        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        StatementImportSettings.ImportAccountId = accounts.First(a => a.Name == choice).Id;
        await RefreshImportAccountNameAsync();
    }

    // ─────────────────────────────────────────────────────────
    //  IMPORT A STATEMENT (PDF / screenshots)
    // ─────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ImportStatementAsync()
    {
        var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/pdf", "image/*" } },
            { DevicePlatform.iOS, new[] { "com.adobe.pdf", "public.image" } },
            { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf", "public.image" } },
            { DevicePlatform.WinUI, new[] { ".pdf", ".png", ".jpg", ".jpeg" } }
        });

        var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Select statement PDF or screenshots",
            FileTypes = types
        });

        var files = picked?.ToList();
        if (files is null || files.Count == 0) return;

        StatementImportResult? result = null;

        await ExecuteAsync(async () =>
        {
            result = await _import.ImportAsync(files, AskPasswordAsync);
            await LoadAsync();
        });

        if (result is null) return;

        if (!result.Success)
        {
            await Shell.Current.DisplayAlert("Import failed", result.Error ?? "Could not read the file.", "OK");
            return;
        }

        var summary =
            $"Rows found: {result.Found}\n" +
            (result.AutoAdded > 0 ? $"Added directly (payees you added before): {result.AutoAdded}\n" : string.Empty) +
            $"Waiting for your review: {result.Added}\n" +
            $"Already in app (skipped): {result.Duplicates}\n" +
            $"Failed / pending (skipped): {result.Skipped}\n" +
            (result.Unreadable > 0 ? $"Could not read: {result.Unreadable}\n" : string.Empty) +
            $"Of these, need a careful look: {result.NeedsReview}\n\n" +
            (result.NewAccounts is { Count: > 0 }
                ? $"New accounts created: {string.Join(", ", result.NewAccounts)}\n" +
                  "(rename them and set the opening balance in Accounts)\n\n"
                : string.Empty) +
            "The file is deleted automatically after 1 day.";

        var hadPayments = result.Found > 0;
        var loans = result.LoanStatements ?? Array.Empty<EmiStatement>();

        if (hadPayments || loans.Count == 0)
            await Shell.Current.DisplayAlert("Import done", summary, "OK");

        foreach (var loan in loans)
            await HandleLoanStatementAsync(loan);
    }

    // ─────────────────────────────────────────────────────────
    //  EMI / LOAN STATEMENT OF ACCOUNT
    // ─────────────────────────────────────────────────────────
    private async Task HandleLoanStatementAsync(EmiStatement statement)
    {
        EmiImportPlan plan;

        try
        {
            plan = await _emiImport.PlanAsync(statement);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("EMI statement", ex.Message, "OK");
            return;
        }

        if (plan.Ambiguous)
        {
            await Shell.Current.DisplayAlert(
                "Which EMI is this?",
                $"{plan.Title}\n\nMore than one of your EMIs looks like this one, so nothing was added. " +
                "Open the right EMI and add the payments there.",
                "OK");
            return;
        }

        // an EMI we already have and every payment is already recorded: nothing to do
        if (!plan.IsNew && plan.ToAdd.Count == 0)
        {
            await Shell.Current.DisplayAlert(
                "EMI is up to date",
                $"{plan.Title}\n\nAll {plan.AlreadyRecorded} paid instalments of this statement are already recorded. Nothing was added.",
                "OK");
            return;
        }

        const string historyOnly = "Add to EMI history only";
        const string withBalance = "Add and also reduce the account balance";

        var pick = await Shell.Current.DisplayActionSheet(
            plan.Summary + "\n\nThese payments were already made. Should they change your account balance?",
            "Cancel",
            null,
            historyOnly,
            withBalance);

        if (pick != historyOnly && pick != withBalance) return;

        EmiImportResult? done = null;

        try
        {
            await ExecuteAsync(async () =>
            {
                done = await _emiImport.ApplyAsync(plan, affectBalance: pick == withBalance);
            });
        }
        catch (InvalidOperationException ex)
        {
            await Shell.Current.DisplayAlert("Could not add", ex.Message, "OK");
            return;
        }

        if (done is not null)
        {
            await Shell.Current.DisplayAlert("EMI done", done.Summary, "OK");
            await ShowNewAccountsAsync();
        }
    }

    private static Task<string?> AskPasswordAsync()
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayPromptAsync(
            "PDF password",
            "This PDF is protected. Enter its password.",
            "Open",
            "Cancel"));

    // ─────────────────────────────────────────────────────────
    //  ADD / SKIP / RENAME
    // ─────────────────────────────────────────────────────────

    /// <summary>Known payee -> adds straight away. New payee -> asks for the category once.</summary>
    [RelayCommand]
    private async Task AddToExpensesAsync(DetectedTransaction item)
    {
        await AddCoreAsync(item);
        await ShowNewAccountsAsync();
    }

    /// <summary>Tells the user when adding an entry created a new account from its account number.</summary>
    private async Task ShowNewAccountsAsync()
    {
        var created = _accountLinks.TakeNewlyCreated();
        if (created.Count == 0) return;

        await Shell.Current.DisplayAlert(
            "New account created",
            string.Join("\n", created) + "\n\nRename it and set its opening balance in Accounts.",
            "OK");
    }

    private async Task AddCoreAsync(DetectedTransaction item)
    {
        // money moved between your own accounts: neither income nor expense
        if (item.SuggestedType == "Transfer")
        {
            var choice = await Shell.Current.DisplayActionSheet(
                "This looks like a transfer between your own accounts",
                "Cancel",
                null,
                "Skip it (own transfer)",
                "Add anyway as plain income/expense");

            if (choice == "Skip it (own transfer)")
            {
                await ExecuteAsync(async () =>
                {
                    await _service.DismissAsync(item);
                    Items.Remove(item);
                    RefreshCounts();
                });

                return;
            }

            if (choice != "Add anyway as plain income/expense") return;
        }
        // a payment to / from a PERSON: becomes a Borrow/Lend record (the Contact is created if it is new)
        else if (item.IsPersonRow)
        {
            string preview;

            try
            {
                preview = await _service.DescribePersonPaymentAsync(item);
            }
            catch (InvalidOperationException ex)
            {
                await Shell.Current.DisplayAlert("Cannot add", ex.Message, "OK");
                return;
            }

            var sameRows = await _service.GetSamePersonRowsAsync(item);

            var options = new List<string> { "Yes, do this" };

            if (sameRows.Count > 1)
                options.Add($"Add all {sameRows.Count} entries of this person (oldest first)");

            options.Add(item.IsCredit ? "Add as plain income instead" : "Add as plain expense instead");

            var pick = await Shell.Current.DisplayActionSheet(preview, "Cancel", null, options.ToArray());

            if (string.IsNullOrEmpty(pick) || pick == "Cancel") return;

            if (pick == "Yes, do this")
            {
                try
                {
                    await ExecuteAsync(async () =>
                    {
                        await _service.AddPersonPaymentAsync(item);
                        Items.Remove(item);
                        RefreshCounts();
                    });
                }
                catch (InvalidOperationException ex)
                {
                    await Shell.Current.DisplayAlert("Could not add", ex.Message, "OK");
                }

                return;
            }

            if (pick.StartsWith("Add all", StringComparison.Ordinal))
            {
                var done = 0;
                string? firstError = null;

                await ExecuteAsync(async () =>
                {
                    // date order matters: a Borrow must exist before a later return can settle it
                    foreach (var row in sameRows)
                    {
                        try
                        {
                            await _service.AddPersonPaymentAsync(row);
                            done++;
                        }
                        catch (InvalidOperationException ex)
                        {
                            firstError ??= ex.Message;
                            break;
                        }
                    }

                    var doneIds = sameRows.Take(done).Select(r => r.Id).ToHashSet();

                    foreach (var shown in Items.Where(i => doneIds.Contains(i.Id)).ToList())
                        Items.Remove(shown);

                    RefreshCounts();
                });

                await Shell.Current.DisplayAlert(
                    firstError is null ? "Done" : "Stopped",
                    firstError is null
                        ? $"{done} entries added to {item.DisplayPayee}."
                        : $"{done} of {sameRows.Count} added. {firstError}",
                    "OK");

                return;
            }

            // "plain income / expense instead" -> carry on with the normal category flow below
        }

        var categories = await _categoryRepo.FindAsync(c => true);

        if (categories.Count == 0)
        {
            await Shell.Current.DisplayAlert("No categories", "Please create a category first.", "OK");
            return;
        }

        int categoryId;

        var remembered = item.SuggestedCategoryId is int id
            ? categories.FirstOrDefault(c => c.Id == id)
            : null;

        if (remembered is not null)
        {
            categoryId = remembered.Id;
        }
        else
        {
            // only Income categories for money in, only Expense categories for money out
            var wantedType = item.IsCredit ? "Income" : "Expense";
            var pool = categories
                .Where(c => string.IsNullOrWhiteSpace(c.Type) ||
                            string.Equals(c.Type, wantedType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pool.Count == 0) pool = categories.ToList();

            var names = pool.Select(c => c.Name).Distinct().ToArray();

            var choice = await Shell.Current.DisplayActionSheet(
                $"{(item.IsCredit ? "+" : "-")}₹{item.Amount:N2} • {item.DisplayPayee}",
                "Cancel",
                null,
                names);

            if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

            categoryId = pool.First(c => c.Name == choice).Id;
        }

        await ExecuteAsync(async () =>
        {
            await _service.AddAsTransactionAsync(item, categoryId);
            Items.Remove(item);
            RefreshCounts();
        });
    }

    /// <summary>Adds every High-confidence Income / Expense / EMI row in one go.</summary>
    [RelayCommand]
    private async Task AddAllConfidentAsync()
    {
        await AddAllCoreAsync();
        await ShowNewAccountsAsync();
    }

    private async Task AddAllCoreAsync()
    {
        if (ConfidentCount == 0) return;

        var ok = await Shell.Current.DisplayAlert(
            "Add all confident",
            $"{ConfidentCount} entries will be added with their suggested category. " +
            "Rows that need review stay in the list.",
            "Add",
            "Cancel");

        if (!ok) return;

        var added = 0;

        await ExecuteAsync(async () =>
        {
            added = await _service.AddAllConfidentAsync();
            await LoadAsync();
        });

        await Shell.Current.DisplayAlert("Done", $"{added} entries added.", "OK");
    }

    [RelayCommand]
    private async Task SkipAsync(DetectedTransaction item)
    {
        await ExecuteAsync(async () =>
        {
            await _service.DismissAsync(item);
            Items.Remove(item);
            RefreshCounts();
        });
    }

    /// <summary>Tap the name -> rename (e.g. "paytmqr281@paytm" -> "Sharma Chai Stall"). Remembered for next time.</summary>
    [RelayCommand]
    private async Task RenamePayeeAsync(DetectedTransaction item)
    {
        var name = await Shell.Current.DisplayPromptAsync(
            "Payee name",
            "Entries will be saved with this name, and it is remembered for the next payment.",
            "Save",
            "Cancel",
            maxLength: 100,
            initialValue: item.Payee ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name)) return;

        await _service.RenamePayeeAsync(item, name);

        // refresh the card
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items.RemoveAt(index);
            Items.Insert(index, item);
        }
    }

    [RelayCommand]
    private async Task EnableAutoDetectAsync()
    {
#if ANDROID && SMS_DETECT
        var status = await Permissions.RequestAsync<SmsReadPermission>();
        NeedsPermission = status != PermissionStatus.Granted;

        if (NeedsPermission)
        {
            await Shell.Current.DisplayAlert(
                "Permission needed",
                "Allow SMS permission from Settings > Apps > MoneySpend > Permissions.",
                "OK");
        }
#else
        await Task.CompletedTask;
#endif
    }
}
