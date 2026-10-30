using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

/// <summary>窗口关闭拦截：弹窗选择最小化到托盘或退出。</summary>
public sealed class WindowCloseService : IWindowCloseService, IAppShutdownParticipant
{
    private readonly ISystemTrayService _trayService;
    private readonly IServiceProvider _services;
    private readonly AppSessionViewModel _session;

    private Window? _mainWindow;
    private bool _isExitInProgress;

    public WindowCloseService(
        ISystemTrayService trayService,
        IServiceProvider services,
        AppSessionViewModel session)
    {
        _trayService = trayService;
        _services = services;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    public bool IsExitInProgress => _isExitInProgress;

    public void PrepareForShutdown()
    {
        _isExitInProgress = true;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_mainWindow != null)
        {
            _mainWindow.Closing -= OnClosing;
            _mainWindow = null;
        }
    }

    public void Attach(Window window)
    {
        _mainWindow = window;
        _trayService.Initialize(window);
        window.Closing += OnClosing;
        RefreshTrayTooltip();
    }

    public async Task RequestExitAsync()
    {
        if (_isExitInProgress || _mainWindow == null || _session.IsShuttingDown)
        {
            return;
        }

        _isExitInProgress = true;
        _trayService.Hide();
        _trayService.Dispose();

        _services.GetRequiredService<AppShutdownService>().Shutdown();

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            Application.Current?.Shutdown();
        }).ConfigureAwait(true);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitInProgress || _session.IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        _ = HandleCloseRequestedAsync();
    }

    private async Task HandleCloseRequestedAsync()
    {
        if (_mainWindow == null || _isExitInProgress || _session.IsShuttingDown)
        {
            return;
        }

        var action = await ResolveCloseActionAsync().ConfigureAwait(true);
        switch (action)
        {
            case CloseWindowAction.Tray:
                MinimizeToTray();
                break;
            case CloseWindowAction.Exit:
                await RequestExitAsync().ConfigureAwait(true);
                break;
        }
    }

    private async Task<CloseWindowAction> ResolveCloseActionAsync()
    {
        if (_session.IsShuttingDown)
        {
            return CloseWindowAction.Ask;
        }

        var result = await CloseChoicePopupService.ShowAsync().ConfigureAwait(true);
        if (result == null || _session.IsShuttingDown)
        {
            return CloseWindowAction.Ask;
        }

        return result.Action == CloseChoiceAction.MinimizeToTray
            ? CloseWindowAction.Tray
            : CloseWindowAction.Exit;
    }

    private void MinimizeToTray()
    {
        if (_mainWindow == null || _session.IsShuttingDown)
        {
            return;
        }

        AppUiDispatcher.InvokeSafe(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Hide();
        });
        RefreshTrayTooltip();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.PollingStatusText)
            or nameof(AppSessionViewModel.IsConnected)
            or nameof(AppSessionViewModel.ConnectionProfileDisplayName)
            or nameof(AppSessionViewModel.ActiveConnectionProfileName)
            or nameof(AppSessionViewModel.Host)
            or nameof(AppSessionViewModel.Port)
            or nameof(AppSessionViewModel.IsMonitoringActive)
            or nameof(AppSessionViewModel.MonitorItemCount)
            or nameof(AppSessionViewModel.LastPollingUpdateLocalTime))
        {
            RefreshTrayTooltip();
        }
    }

    private void RefreshTrayTooltip()
    {
        if (_session.IsShuttingDown || !_trayService.IsVisible)
        {
            return;
        }

        _trayService.UpdateTooltip(TrayStatusFormatter.BuildTooltip(_session));
    }
}
