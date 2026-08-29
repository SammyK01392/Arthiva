using Arthiva.Data;

namespace Arthiva;

public partial class App : Application
{
    private readonly ArthivaDatabase _database;

    public App(ArthivaDatabase database, IServiceProvider serviceProvider)
    {
        InitializeComponent();          // loads Colors.xaml/Styles.xaml into App resources FIRST

        _database = database;
        MainPage = serviceProvider.GetRequiredService<AppShell>();   // AppShell built AFTER resources exist
    }

    protected override void OnStart()
        => _ = InitializeDatabaseAsync();

    private async Task InitializeDatabaseAsync()
    {
        await _database.InitializeAsync();
    }
}