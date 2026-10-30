using System.Windows;
using System.Windows.Controls;
using S7PlusMonitor.Pages;
using S7PlusMonitor.Services;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor;

public partial class MainWindow : Window
{
    private readonly AppSessionViewModel _session;
    private readonly MonitorPageViewModel _monitor;
    private readonly ConnectionViewModel _connection;

    public MainWindow(
        AppSessionViewModel session,
        MonitorPageViewModel monitor,
        ConnectionViewModel connection,
        MonitorPage monitorPage,
        BrowsePage browsePage,
        ConnectionPage connectionPage,
        IWindowCloseService closeService)
    {
        _session = session;
        _monitor = monitor;
        _connection = connection;
        InitializeComponent();

        ((TabItem)MainTabs.Items[0]).Content = connectionPage;
        ((TabItem)MainTabs.Items[1]).Content = browsePage;
        ((TabItem)MainTabs.Items[2]).Content = monitorPage;
        MainTabs.SelectedIndex = 0;

        AppNavigation.RequestTab = NavigateToTab;
        closeService.Attach(this);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _session.InitializeAsync().ConfigureAwait(true);
        await _connection.InitializeAsync().ConfigureAwait(true);
        await _monitor.InitializeAsync().ConfigureAwait(true);
    }

    private void NavigateToTab(string route)
    {
        for (var i = 0; i < MainTabs.Items.Count; i++)
        {
            if (MainTabs.Items[i] is TabItem { Tag: string tag } &&
                string.Equals(tag, route, StringComparison.OrdinalIgnoreCase))
            {
                MainTabs.SelectedIndex = i;
                return;
            }
        }
    }
}
