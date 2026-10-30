using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

/// <summary>应用退出时释放 PLC 连接、轮询循环与本地资源。</summary>
public sealed class AppShutdownService
{
    private int _shutdownStarted;

    private readonly IReadOnlyList<IAppShutdownParticipant> _participants;
    private readonly AppSessionViewModel _session;
    private readonly PollingMonitorEngine _pollingEngine;
    private readonly SqliteAppDatabase _database;
    private readonly ISystemTrayService _trayService;

    public AppShutdownService(
        AppSessionViewModel session,
        MonitorPageViewModel monitorPage,
        TrendWorkspaceViewModel trendWorkspace,
        BrowseViewModel browseViewModel,
        ConnectionViewModel connectionViewModel,
        WindowCloseService windowCloseService,
        PollingMonitorEngine pollingEngine,
        SqliteAppDatabase database,
        ISystemTrayService trayService)
    {
        _session = session;
        _pollingEngine = pollingEngine;
        _database = database;
        _trayService = trayService;
        _participants =
        [
            session,
            monitorPage,
            trendWorkspace,
            browseViewModel,
            connectionViewModel,
            windowCloseService
        ];
    }

    public void Shutdown()
    {
        try
        {
            if (AppUiDispatcher.CheckAccess())
            {
                Task.Run(ShutdownCore).Wait(TimeSpan.FromSeconds(8));
            }
            else
            {
                ShutdownCore();
            }
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException))
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        ShutdownAsyncCore(cancellationToken);

    private void ShutdownCore() =>
        ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();

    private async Task ShutdownAsyncCore(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
        {
            return;
        }

        TrendSettingsPopupService.CloseAll();
        CloseChoicePopupService.CloseAll();
        _trayService.Hide();
        _trayService.Dispose();

        foreach (var participant in _participants)
        {
            participant.PrepareForShutdown();
        }

        try
        {
            await _pollingEngine.StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _session.ShutdownForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }
}
