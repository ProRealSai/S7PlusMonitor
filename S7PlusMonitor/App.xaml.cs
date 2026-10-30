using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Export;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.Pages;
using S7PlusMonitor.Services;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SqliteNative.Initialize();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<AppShutdownService>().Shutdown();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "S7PlusMonitor");
        System.IO.Directory.CreateDirectory(dataDir);

        services.AddSingleton<ISecureCredentialStore>(_ =>
            new DpapiSecureCredentialStore(System.IO.Path.Combine(dataDir, "credentials")));
        services.AddSingleton(sp =>
            new SqliteAppDatabase(
                System.IO.Path.Combine(dataDir, AppConstants.DatabaseFileName),
                sp.GetRequiredService<ISecureCredentialStore>()));
        services.AddSingleton<IPlcConnectionRepository>(sp => sp.GetRequiredService<SqliteAppDatabase>());
        services.AddSingleton<IAppSettingsRepository>(sp => sp.GetRequiredService<SqliteAppDatabase>());
        services.AddSingleton<IMonitorSheetRepository>(sp => sp.GetRequiredService<SqliteAppDatabase>());
        services.AddSingleton<ITrendSampleRepository>(sp => sp.GetRequiredService<SqliteAppDatabase>());
        services.AddSingleton<ITrendViewRepository>(sp => sp.GetRequiredService<SqliteAppDatabase>());
        services.AddSingleton<IPlcService, S7CommPlusPlcService>();
        services.AddSingleton<PollingMonitorEngine>();
        services.AddSingleton<IVariableListExcelExporter, VariableListExcelExporter>();
        services.AddSingleton<ISystemTrayService, WindowsSystemTrayService>();
        services.AddSingleton<WindowCloseService>();
        services.AddSingleton<IWindowCloseService>(sp => sp.GetRequiredService<WindowCloseService>());
        services.AddSingleton<AppShutdownService>();

        services.AddSingleton<AppSessionViewModel>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<TrendWorkspaceViewModel>();
        services.AddSingleton<MonitorPageViewModel>();

        services.AddSingleton<MonitorPage>();
        services.AddSingleton<BrowsePage>();
        services.AddSingleton<ConnectionPage>();
        services.AddSingleton<MainWindow>();
    }
}
