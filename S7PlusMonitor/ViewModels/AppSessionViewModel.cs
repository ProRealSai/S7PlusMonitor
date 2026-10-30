using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.Services;

namespace S7PlusMonitor.ViewModels;

public partial class AppSessionViewModel : ObservableObject, IAppShutdownParticipant
{
    private readonly IPlcService _plcService;
    private readonly IPlcConnectionRepository _connectionRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly PollingSession _pollingSession;
    private bool _isInitialized;
    private bool _isShuttingDown;
    private bool _isSelectionChangeSuppressed;

    public AppSessionViewModel(
        IPlcService plcService,
        IPlcConnectionRepository connectionRepository,
        IAppSettingsRepository appSettings,
        PollingMonitorEngine pollingEngine)
    {
        _plcService = plcService;
        _connectionRepository = connectionRepository;
        _appSettings = appSettings;
        _pollingSession = new PollingSession(pollingEngine, () => IsConnected);
        _pollingSession.StatusUpdated += OnPollingSessionStatusUpdated;
    }

    private void OnPollingSessionStatusUpdated()
    {
        OnPropertyChanged(nameof(PollingStatusText));
        OnPropertyChanged(nameof(HasPollingStatus));
        OnPropertyChanged(nameof(MonitorItemCount));
        OnPropertyChanged(nameof(LastPollingUpdateLocalTime));
        OnPropertyChanged(nameof(StatusBarToolTip));
    }

    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>退出清理前标记，避免销毁阶段再调度 UI 更新。</summary>
    public void PrepareForShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _pollingSession.DetachFromEngine();
        _pollingSession.StatusUpdated -= OnPollingSessionStatusUpdated;
    }

    public ObservableCollection<PlcConnectionEntity> Connections { get; } = [];

    [ObservableProperty]
    private int _connectionsVersion;

    public string ConnectionStateDot => IsConnected ? "●" : "○";

    public string ConnectionEndpointText =>
        IsConnected ? $"已连接 {Host}:{Port}" : "未连接";

    public string ConnectionStatusToolTip =>
        IsConnected ? $"已连接到 {Host}:{Port}" : "当前未连接 PLC";

    public string StatusBarToolTip
    {
        get
        {
            var polling = HasPollingStatus ? PollingStatusText : "监视未运行";
            return $"{StatusMessage}{Environment.NewLine}{ConnectionStatusToolTip}{Environment.NewLine}{polling}";
        }
    }

    public string SelectedConnectionDisplayName =>
        SelectedConnection?.Name ?? "未选择档案";

    /// <summary>状态栏轮询文本（由 <see cref="PollingSession"/> 计算）。</summary>
    public string PollingStatusText => _pollingSession.PollingStatusText;

    public bool HasPollingStatus => _pollingSession.HasPollingStatus;

    public bool IsMonitoringActive =>
        IsConnected && _pollingSession.IsRunning && !_pollingSession.IsSuspended;

    public bool CanStartMonitoring =>
        IsConnected && IsConnectionLocked && !IsMonitoringActive;

    public bool CanStopMonitoring =>
        IsConnected && _pollingSession.IsRunning;

    public int MonitorItemCount => _pollingSession.LastPolledItemCount;

    public DateTime? LastPollingUpdateLocalTime => _pollingSession.LastPollingUpdateLocalTime;

    [ObservableProperty]
    private PlcConnectionEntity? _selectedConnection;

    [ObservableProperty]
    private string _host = "192.168.0.1";

    [ObservableProperty]
    private int _port = 102;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public bool CanConnect => !IsConnected && !IsBusy;

    public bool CanDisconnect => IsConnected && !IsBusy;

    /// <summary>已连接 PLC 且已选中连接档案。</summary>
    public bool HasActiveSession => IsConnectionLocked;

    /// <summary>连接成功时锁定的档案 ID；断开前不可切换档案。</summary>
    public int? ConnectedConnectionId { get; private set; }

    /// <summary>连接成功时锁定的档案名称。</summary>
    public string ConnectedConnectionName { get; private set; } = string.Empty;

    /// <summary>已连接且会话已锁定到某一档案。</summary>
    public bool IsConnectionLocked => IsConnected && ConnectedConnectionId is > 0;

    /// <summary>顶栏/面板显示的档案名：已连接时显示锁定档案，否则显示当前选中。</summary>
    public string ConnectionProfileDisplayName =>
        IsConnectionLocked
            ? ConnectedConnectionName
            : SelectedConnection?.Name ?? "未选择档案";

    /// <summary>监视/浏览/趋势面板使用的档案名（仅会话激活时有值）。</summary>
    public string ActiveConnectionProfileName =>
        IsConnectionLocked ? ConnectedConnectionName : string.Empty;

    public event EventHandler? Disconnected;

    public event EventHandler? SelectedConnectionApplied;

    public event EventHandler? TrendMembershipChanged;

    public event EventHandler<int>? TrendHistoryCleared;

    public event EventHandler? TrendViewSettingsChanged;

    private HashSet<string>? _browsedSymbols;

    /// <summary>最近一次浏览得到的 PLC 符号表（OrdinalIgnoreCase）。</summary>
    public void SetBrowsedSymbols(IEnumerable<string> symbols)
    {
        _browsedSymbols = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
    }

    public void ClearBrowsedSymbols() => _browsedSymbols = null;

    /// <summary>符号是否存在于最近一次浏览结果中；未浏览时视为未知（不标记缺失）。</summary>
    public bool IsSymbolMissingFromBrowse(string symbol) =>
        _browsedSymbols is { Count: > 0 } set && !set.Contains(symbol);

    /// <summary>
    /// 广播监视表内容变更（A2 解耦）：原 C# 事件改为 WeakReferenceMessenger 消息，
    /// 保证 MonitorPage/TrendWorkspace 单一订阅且弱引用不泄漏。
    /// </summary>
    public void NotifyMonitorSheetChanged()
    {
        _pollingSession.InvalidateSymbolCache();
        WeakReferenceMessenger.Default.Send(new MonitorSheetChangedMessage(ConnectedConnectionId));
    }

    public void NotifyTrendMembershipChanged() =>
        TrendMembershipChanged?.Invoke(this, EventArgs.Empty);

    public void NotifyTrendHistoryCleared(int plcConnectionId) =>
        TrendHistoryCleared?.Invoke(this, plcConnectionId);

    public void NotifyTrendViewSettingsChanged() =>
        TrendViewSettingsChanged?.Invoke(this, EventArgs.Empty);

    public async Task<int?> GetActiveConnectionIdAsync()
    {
        if (IsConnectionLocked)
        {
            return ConnectedConnectionId;
        }

        if (SelectedConnection is { Id: > 0 } selected)
        {
            return selected.Id;
        }

        var activeIdText = await _appSettings.GetAsync("active_connection_id").ConfigureAwait(false);
        return int.TryParse(activeIdText, out var activeId) && activeId > 0 ? activeId : null;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await ReloadConnectionsInPlaceAsync().ConfigureAwait(false);

        var activeIdText = await _appSettings.GetAsync("active_connection_id").ConfigureAwait(false);
        PlcConnectionEntity? selected = null;
        if (int.TryParse(activeIdText, out var activeId))
        {
            selected = Connections.FirstOrDefault(item => item.Id == activeId);
        }

        selected ??= Connections.FirstOrDefault(item => item.IsDefault) ?? Connections.FirstOrDefault();
        if (selected != null)
        {
            await RunUiAsync(() => SelectedConnection = selected).ConfigureAwait(true);
        }
    }

    partial void OnSelectedConnectionChanged(PlcConnectionEntity? value)
    {
        OnPropertyChanged(nameof(SelectedConnectionDisplayName));
        OnPropertyChanged(nameof(ConnectionProfileDisplayName));
        OnPropertyChanged(nameof(HasActiveSession));
        if (_isSelectionChangeSuppressed)
        {
            return;
        }

        if (IsConnectionLocked && value?.Id != ConnectedConnectionId)
        {
            RestoreLockedConnectionSelection();
            StatusMessage = "已连接 PLC，请先断开再切换连接档案。";
            return;
        }

        _ = ApplySelectedConnectionAsync(value);
    }

    private void RestoreLockedConnectionSelection()
    {
        if (ConnectedConnectionId is not int lockedId || lockedId <= 0)
        {
            return;
        }

        var locked = Connections.FirstOrDefault(item => item.Id == lockedId);
        if (locked == null || ReferenceEquals(SelectedConnection, locked))
        {
            return;
        }

        _isSelectionChangeSuppressed = true;
        try
        {
            SelectedConnection = locked;
        }
        finally
        {
            _isSelectionChangeSuppressed = false;
        }
    }

    private void LockConnectionSession(PlcConnectionEntity connection)
    {
        ConnectedConnectionId = connection.Id;
        ConnectedConnectionName = connection.Name;
        NotifyConnectionLockChanged();
    }

    private void UnlockConnectionSession()
    {
        ConnectedConnectionId = null;
        ConnectedConnectionName = string.Empty;
        NotifyConnectionLockChanged();
    }

    private void NotifyConnectionLockChanged()
    {
        OnPropertyChanged(nameof(ConnectedConnectionId));
        OnPropertyChanged(nameof(ConnectedConnectionName));
        OnPropertyChanged(nameof(IsConnectionLocked));
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(ConnectionProfileDisplayName));
        OnPropertyChanged(nameof(ActiveConnectionProfileName));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        AppUiDispatcher.InvokeSafe(() =>
        {
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ConnectionStateDot));
            OnPropertyChanged(nameof(ConnectionEndpointText));
            OnPropertyChanged(nameof(ConnectionStatusToolTip));
            OnPropertyChanged(nameof(StatusBarToolTip));
            OnPropertyChanged(nameof(ConnectionProfileDisplayName));
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(IsConnectionLocked));
            OnPropertyChanged(nameof(ActiveConnectionProfileName));
        });
        _pollingSession.NotifyStatusChanged(force: true);
        if (!value)
        {
            UnlockConnectionSession();
            if (!_isShuttingDown && _pollingSession.IsRunning)
            {
                _ = StopMonitoringAsync();
            }
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        AppUiDispatcher.InvokeSafe(() =>
        {
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        });
    }

    partial void OnHostChanged(string value)
    {
        OnPropertyChanged(nameof(ConnectionEndpointText));
        OnPropertyChanged(nameof(ConnectionStatusToolTip));
        OnPropertyChanged(nameof(StatusBarToolTip));
    }

    partial void OnPortChanged(int value)
    {
        OnPropertyChanged(nameof(ConnectionEndpointText));
        OnPropertyChanged(nameof(ConnectionStatusToolTip));
        OnPropertyChanged(nameof(StatusBarToolTip));
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(StatusBarToolTip));

    public async Task ReloadConnectionsInPlaceAsync()
    {
        var all = await _connectionRepository.GetAllAsync().ConfigureAwait(false);
        await RunUiAsync(() =>
        {
            UpsertConnections(all);
            RemoveMissingConnections(all);
            ReorderConnections(all);
            ConnectionsVersion++;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 先 upsert 再让调用方改 SelectedItem，最后再删过期项。
    /// WinUI Picker 不能在 ItemsSource 仍有项时把 SelectedItem 置空，也不能先删当前选中项。
    /// </summary>
    public async Task<PlcConnectionEntity?> RefreshConnectionsAndSelectAsync(
        int? connectionId,
        Action<PlcConnectionEntity?>? onItemAvailable = null)
    {
        var all = await _connectionRepository.GetAllAsync().ConfigureAwait(false);
        PlcConnectionEntity? selected = null;
        await RunUiAsync(() =>
        {
            UpsertConnections(all);
            selected = connectionId is > 0
                ? Connections.FirstOrDefault(item => item.Id == connectionId)
                : null;
            if (selected != null)
            {
                onItemAvailable?.Invoke(selected);
                RemoveMissingConnections(all);
            }
            else
            {
                RemoveMissingConnections(all);
                onItemAvailable?.Invoke(null);
            }
            ReorderConnections(all);
            ConnectionsVersion++;
        }).ConfigureAwait(false);

        await ApplySelectedConnectionAsync(selected, notifyListeners: false).ConfigureAwait(false);
        return selected;
    }

    private void UpsertConnections(IReadOnlyList<PlcConnectionEntity> all)
    {
        foreach (var fresh in all)
        {
            var existing = Connections.FirstOrDefault(item => item.Id == fresh.Id);
            if (existing != null)
            {
                PlcConnectionEntityMapper.CopyFields(fresh, existing);
            }
            else
            {
                Connections.Add(fresh);
            }
        }
    }

    private void RemoveMissingConnections(IReadOnlyList<PlcConnectionEntity> all)
    {
        var idSet = all.Select(item => item.Id).ToHashSet();
        for (var index = Connections.Count - 1; index >= 0; index--)
        {
            if (!idSet.Contains(Connections[index].Id))
            {
                Connections.RemoveAt(index);
            }
        }
    }

    private void ReorderConnections(IReadOnlyList<PlcConnectionEntity> all)
    {
        for (var target = 0; target < all.Count; target++)
        {
            var id = all[target].Id;
            var current = -1;
            for (var i = 0; i < Connections.Count; i++)
            {
                if (Connections[i].Id == id)
                {
                    current = i;
                    break;
                }
            }

            if (current >= 0 && current != target)
            {
                Connections.Move(current, target);
            }
        }
    }

    public async Task ApplySelectedConnectionAsync(
        PlcConnectionEntity? connection,
        bool notifyListeners = true)
    {
        var activeId = connection is { Id: > 0 } ? connection.Id.ToString() : string.Empty;
        await RunUiAsync(() =>
        {
            _isSelectionChangeSuppressed = true;
            try
            {
                SelectedConnection = connection;
                if (connection is { Id: > 0 })
                {
                    Host = connection.Host;
                    Port = connection.Port;
                    if (notifyListeners)
                    {
                        StatusMessage = $"已选择连接档案：{connection.Name}";
                    }
                }
            }
            finally
            {
                _isSelectionChangeSuppressed = false;
            }
        }).ConfigureAwait(true);

        await _appSettings.SetAsync("active_connection_id", activeId).ConfigureAwait(false);

        if (notifyListeners)
        {
            SelectedConnectionApplied?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpdateConnectionEndpoint(string host, int port)
    {
        Host = host.Trim();
        Port = port;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task ConnectAsync(PlcConnectionSettings? settings = null)
    {
        settings ??= await BuildConnectionSettingsFromSessionAsync().ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            StatusMessage = "请输入 PLC IP 地址。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在连接...";

        try
        {
            await StopMonitoringAsync().ConfigureAwait(true);
            await _plcService.ConnectAsync(settings).ConfigureAwait(true);

            // 先置 IsConnected，再锁定档案：IsConnectionLocked = IsConnected && Id>0。
            // 若先 Lock 再连，锁定通知时仍未连接，趋势图会当成「未连接 PLC」。
            IsConnected = true;
            if (SelectedConnection is { Id: > 0 } lockedConnection)
            {
                LockConnectionSession(lockedConnection);
                await _connectionRepository.UpdateLastUsedAsync(lockedConnection.Id).ConfigureAwait(true);
                await _appSettings.SetAsync("active_connection_id", lockedConnection.Id.ToString()).ConfigureAwait(true);
                StatusMessage = $"已连接到 {settings.Host}:{settings.Port}（档案：{lockedConnection.Name}）";
                NotifyMonitoringStateChanged();
            }
            else
            {
                UnlockConnectionSession();
                StatusMessage = $"已连接到 {settings.Host}:{settings.Port}（未绑定档案，监视表不可用）";
                NotifyMonitoringStateChanged();
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            UnlockConnectionSession();
            ClearBrowsedSymbols();
            await StopMonitoringAsync().ConfigureAwait(true);
            StatusMessage = $"连接失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    public async Task DisconnectAsync()
    {
        IsBusy = true;
        StatusMessage = "正在断开...";

        try
        {
            await StopMonitoringAsync().ConfigureAwait(true);
            await _plcService.DisconnectAsync().ConfigureAwait(true);
            IsConnected = false;
            UnlockConnectionSession();
            ClearBrowsedSymbols();
            Disconnected?.Invoke(this, EventArgs.Empty);
            StatusMessage = "已断开连接";
        }
        catch (OperationCanceledException)
        {
            IsConnected = false;
            UnlockConnectionSession();
            ClearBrowsedSymbols();
            Disconnected?.Invoke(this, EventArgs.Empty);
            StatusMessage = "已断开连接";
        }
        catch (Exception ex)
        {
            StatusMessage = $"断开失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToConnectionTabAsync()
    {
        await AppNavigation.GoToAsync("connection").ConfigureAwait(true);
    }

    private async Task<PlcConnectionSettings> BuildConnectionSettingsFromSessionAsync()
    {
        var autoFallbackText = await _appSettings
            .GetAsync(AppConstants.AutoSecurityFallbackKey)
            .ConfigureAwait(false);

        string? password = null;
        if (SelectedConnection is { Id: > 0 } conn)
        {
            try
            {
                password = await _connectionRepository.GetPasswordAsync(conn.Id).ConfigureAwait(false);
            }
            catch
            {
                // 密码读取失败则按空密码继续，由驱动层报错
            }
        }

        return new PlcConnectionSettings
        {
            Host = Host.Trim(),
            Port = Port,
            TimeoutSeconds = SelectedConnection?.TimeoutSeconds ?? 10,
            Username = string.IsNullOrWhiteSpace(SelectedConnection?.Username)
                ? null
                : SelectedConnection.Username.Trim(),
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            SecurityMode = PlcConnectionMapper.ParseSecurityMode(SelectedConnection?.SecurityMode ?? "Auto"),
            AutoFallbackMode = AutoSecurityFallbackHelper.ParseOrDefault(autoFallbackText)
        };
    }

    /// <summary>浏览等独占 PLC 操作期间暂停监视读取。</summary>
    public void SuspendMonitoring()
    {
        _pollingSession.SetSuspended(true);
        NotifyMonitoringStateChanged();
        _pollingSession.NotifyStatusChanged(force: true);
    }

    /// <summary>恢复监视读取；若已连接且引擎未运行则启动。</summary>
    public async Task ResumeMonitoringAsync()
    {
        _pollingSession.SetSuspended(false);
        if (IsConnected && !_pollingSession.IsRunning)
        {
            await StartMonitoringAsync().ConfigureAwait(false);
            return;
        }

        await RunUiAsync(() =>
        {
            NotifyMonitoringStateChangedCore();
            _pollingSession.NotifyStatusChanged(force: true);
        }).ConfigureAwait(false);
    }

    /// <summary>EnsureMonitoringAsync 在浏览/连接忙碌或轮询已挂起时不强行启动。</summary>
    public async Task EnsureMonitoringAsync()
    {
        if (!IsConnectionLocked || IsBusy)
        {
            return;
        }

        if (_pollingSession.IsSuspended)
        {
            return;
        }

        if (!_pollingSession.IsRunning)
        {
            await StartMonitoringAsync().ConfigureAwait(false);
        }
    }

    /// <summary>连接成功且浏览完成后启动监视表周期读取。</summary>
    public async Task StartMonitoringAsync()
    {
        if (!IsConnected)
        {
            _pollingSession.NotifyStatusChanged(force: true);
            return;
        }

        if (ConnectedConnectionId is not int connectionId || connectionId <= 0)
        {
            _pollingSession.NotifyStatusChanged(force: true);
            return;
        }

        _pollingSession.SetSuspended(false);
        if (_pollingSession.IsRunning)
        {
            await RunUiAsync(() =>
            {
                NotifyMonitoringStateChangedCore();
                _pollingSession.NotifyStatusChanged(force: true);
            }).ConfigureAwait(false);
            return;
        }

        await _pollingSession.StartAsync(connectionId).ConfigureAwait(false);
        _pollingSession.SetSuspended(false);
        await RunUiAsync(() =>
        {
            NotifyMonitoringStateChangedCore();
            _pollingSession.NotifyStatusChanged(force: true);
        }).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
    private Task StartMonitorAsync() => StartMonitoringAsync();

    [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
    private Task StopMonitorAsync() => StopMonitoringManuallyAsync();

    public Task StopMonitoringManuallyAsync() => StopMonitoringAsync();

    private void NotifyMonitoringStateChanged() =>
        RunUi(NotifyMonitoringStateChangedCore);

    /// <summary>应用退出时停止轮询并断开 PLC。</summary>
    public async Task ShutdownForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await StopMonitoringAsync(updateUi: false).ConfigureAwait(false);
            if (_plcService.IsConnected)
            {
                await _plcService.DisconnectForShutdownAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // 退出阶段尽力释放
        }
    }

    private async Task StopMonitoringAsync(bool updateUi = true)
    {
        try
        {
            await _pollingSession.StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 停止轮询时取消进行中的 PLC 读操作
        }

        if (!updateUi || _isShuttingDown)
        {
            return;
        }

        await RunUiAsync(() =>
        {
            NotifyMonitoringStateChangedCore();
            _pollingSession.NotifyStatusChanged(force: true);
        }).ConfigureAwait(false);
    }

    private void NotifyMonitoringStateChangedCore()
    {
        OnPropertyChanged(nameof(IsMonitoringActive));
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        StartMonitorCommand.NotifyCanExecuteChanged();
        StopMonitorCommand.NotifyCanExecuteChanged();
    }

    private void RunUi(Action action)
    {
        if (_isShuttingDown)
        {
            return;
        }

        AppUiDispatcher.InvokeSafe(action);
    }

    private Task RunUiAsync(Action action)
    {
        if (_isShuttingDown)
        {
            return Task.CompletedTask;
        }

        return AppUiDispatcher.InvokeSafeAwaitAsync(action);
    }
}
