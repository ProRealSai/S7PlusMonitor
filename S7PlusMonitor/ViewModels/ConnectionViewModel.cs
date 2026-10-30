using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;
using S7PlusMonitor.Services;

namespace S7PlusMonitor.ViewModels;

/// <summary>连接 Tab 表单 VM：档案 CRUD + 全局 PollInterval 同步到 trend_views。</summary>

public partial class ConnectionViewModel : ObservableObject, IAppShutdownParticipant
{
    private readonly AppSessionViewModel _session;
    private readonly IPlcConnectionRepository _connectionRepository;
    private readonly IMonitorSheetRepository _monitorSheetRepository;
    private readonly ITrendSampleRepository _trendSampleRepository;
    private readonly ITrendViewRepository _trendViewRepository;
    private readonly BrowseViewModel _browseViewModel;
    private readonly IAppSettingsRepository _appSettings;
    private bool _isConnectionSelectionHandlingSuppressed;
    private bool _isInitialized;
    private int _formLoadSerial;
    private bool _isCreatingNewProfile;
    private (DatabaseFileStats Stats, DateTimeOffset AtUtc, int ConnectionId)? _lastStorageStats;

    public ConnectionViewModel(
        AppSessionViewModel session,
        IPlcConnectionRepository connectionRepository,
        IMonitorSheetRepository monitorSheetRepository,
        ITrendSampleRepository trendSampleRepository,
        ITrendViewRepository trendViewRepository,
        BrowseViewModel browseViewModel,
        IAppSettingsRepository appSettings)
    {
        _session = session;
        _connectionRepository = connectionRepository;
        _monitorSheetRepository = monitorSheetRepository;
        _trendSampleRepository = trendSampleRepository;
        _trendViewRepository = trendViewRepository;
        _browseViewModel = browseViewModel;
        _appSettings = appSettings;
        SecurityModes = ["Auto", "Tls", "LegacyChallenge"];
        AutoFallbackModeOptions =
        [
            new AutoFallbackModeOption { Label = "驱动内置", Mode = AutoSecurityFallbackMode.Driver },
            new AutoFallbackModeOption { Label = "应用两次连接", Mode = AutoSecurityFallbackMode.Application }
        ];
        SelectedAutoFallbackModeOption = AutoFallbackModeOptions[0];
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    public void PrepareForShutdown() =>
        _session.PropertyChanged -= OnSessionPropertyChanged;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.ConnectionsVersion))
        {
            OnPropertyChanged(nameof(ConnectionPickerGeneration));
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.IsBusy)
            or nameof(AppSessionViewModel.IsConnected)
            or nameof(AppSessionViewModel.IsConnectionLocked)
            or nameof(AppSessionViewModel.StatusMessage))
        {
            NotifySessionDerivedProperties();
        }
    }

    public IReadOnlyList<string> SecurityModes { get; }

    public IReadOnlyList<AutoFallbackModeOption> AutoFallbackModeOptions { get; }

    [ObservableProperty]
    private AutoFallbackModeOption? _selectedAutoFallbackModeOption;

    public ObservableCollection<PlcConnectionEntity> Connections => _session.Connections;

    public int ConnectionPickerGeneration => _session.ConnectionsVersion;

    [ObservableProperty]
    private PlcConnectionEntity? _selectedConnection;

    [ObservableProperty]
    private string _connectionName = string.Empty;

    [ObservableProperty]
    private string _host = "192.168.0.1";

    [ObservableProperty]
    private int _port = 102;

    [ObservableProperty]
    private int _timeoutSeconds = 10;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    public bool IsPasswordMasked => !IsPasswordVisible;

    public string PasswordVisibilityToggleText => IsPasswordVisible ? "🙈" : "👁";

    public string PasswordVisibilityToggleHint =>
        IsPasswordVisible ? "隐藏密码" : "显示密码";

    [ObservableProperty]
    private string _securityMode = "Auto";

    [ObservableProperty]
    private bool _isDefaultConnection;

    [ObservableProperty]
    private int _pollIntervalMs = AppConstants.DefaultPollIntervalMs;

    [ObservableProperty]
    private string _databaseFileSizeText = "—";

    [ObservableProperty]
    private string _connectionHistoryStorageText = "—";

    public string DatabaseStorageHint =>
        "全部连接共用一个 SQLite 文件 s7plus_monitor.db；下方为当前档案的历史采样占用。";

    public string PollIntervalHint =>
        $"监视表轮询间隔（毫秒 ms），{AppConstants.MinPollIntervalMs}–{AppConstants.MaxPollIntervalMs}；0=连续轮询（按 {AppConstants.EffectiveMinPollIntervalMs}ms 下限执行，防混叠）；重新连接后生效";

    public string TimeoutHint =>
        "连接/读写超时（秒 s），非毫秒；驱动 Auto 单次连接总预算约 2×该值";

    public string SecurityModeHint => SecurityMode switch
    {
        "Tls" => "Tls：仅 TLS 加密握手（TIA 已启用 Secure Communication）",
        "LegacyChallenge" => "LegacyChallenge：旧 CPU 或未配置 TLS 的工程",
        _ => "Auto（推荐）：先 TLS，失败则回退 Legacy Challenge"
    };

    public bool IsAutoSecurityMode => SecurityMode == "Auto";

    public string AutoFallbackModeHint => SelectedAutoFallbackModeOption?.Mode switch
    {
        AutoSecurityFallbackMode.Application =>
            "TLS 失败后断开并以 Legacy Challenge 独立重连；适合驱动 Auto 无效的旧 PLC",
        _ => "驱动在一次 Connect 内完成 TLS→Legacy 回退（默认）"
    };

    public bool IsBusy => _session.IsBusy;

    public bool IsConnected => _session.IsConnected;

    public bool IsConnectionLocked => _session.IsConnectionLocked;

    public bool CanEditConnectionProfile => !IsConnectionLocked && !IsBusy;

    public string ConnectionLockHint =>
        IsConnectionLocked
            ? $"已连接档案「{_session.ConnectedConnectionName}」，请先断开再切换或保存档案。"
            : string.Empty;

    public string StatusMessage => _session.StatusMessage;

    public bool CanConnect => _session.CanConnect;

    public bool CanDisconnect => _session.CanDisconnect;

    public bool CanSaveConnection => CanEditConnectionProfile;

    public bool CanBrowse => IsConnected && !IsBusy;

    public bool CanClearConnectionHistory =>
        !IsBusy && SelectedConnection is { Id: > 0 } && !_isCreatingNewProfile;

    public bool IsCreatingNewProfile => _isCreatingNewProfile;

    public string NewProfileHint =>
        _isCreatingNewProfile
            ? "正在新建档案（尚未保存）。保存后会出现在下拉列表中；选择其它档案将放弃本次新建。"
            : string.Empty;

    partial void OnSelectedConnectionChanged(PlcConnectionEntity? value)
    {
        if (_isConnectionSelectionHandlingSuppressed)
        {
            return;
        }

        if (IsConnectionLocked && value?.Id != _session.ConnectedConnectionId)
        {
            var locked = Connections.FirstOrDefault(item => item.Id == _session.ConnectedConnectionId);
            _isConnectionSelectionHandlingSuppressed = true;
            SelectedConnection = locked;
            _isConnectionSelectionHandlingSuppressed = false;
            _session.StatusMessage = "已连接 PLC，请先断开再切换连接档案。";
            return;
        }

        ExitCreateMode();
        _session.SelectedConnection = value;
        OnPropertyChanged(nameof(CanClearConnectionHistory));
        ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
        if (value != null)
        {
            _ = ApplyConnectionToFormAsync(value);
        }

        _ = RefreshStorageStatsAsync();
    }

    partial void OnConnectionNameChanged(string value) => SaveConnectionCommand.NotifyCanExecuteChanged();

    partial void OnHostChanged(string value)
    {
        SaveConnectionCommand.NotifyCanExecuteChanged();
        _session.UpdateConnectionEndpoint(Host, Port);
    }

    partial void OnPortChanged(int value) => _session.UpdateConnectionEndpoint(Host, Port);

    partial void OnSecurityModeChanged(string value)
    {
        OnPropertyChanged(nameof(SecurityModeHint));
        OnPropertyChanged(nameof(IsAutoSecurityMode));
    }

    partial void OnSelectedAutoFallbackModeOptionChanged(AutoFallbackModeOption? value) =>
        OnPropertyChanged(nameof(AutoFallbackModeHint));

    public async Task InitializeAsync()
    {
        await _session.InitializeAsync().ConfigureAwait(false);
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadPollIntervalAsync().ConfigureAwait(false);
        await LoadAutoFallbackModeAsync().ConfigureAwait(false);
        var selected = _session.SelectedConnection;
        await AppUiDispatcher.InvokeSafeAwaitAsync(() => SelectedConnection = selected).ConfigureAwait(true);
        if (SelectedConnection != null)
        {
            await ApplyConnectionToFormAsync(SelectedConnection).ConfigureAwait(true);
        }

        await RefreshStorageStatsAsync().ConfigureAwait(true);
    }

    private async Task RefreshStorageStatsAsync(bool force = false)
    {
        var connectionId = SelectedConnection is { Id: > 0 } selected ? selected.Id : 0;
        // D6：SUM(LENGTH(...)) 全表扫描在 1M 采样时约 800ms，5 秒内复用同一档案的缓存
        if (!force
            && _lastStorageStats is { } cached
            && cached.ConnectionId == connectionId
            && (DateTimeOffset.UtcNow - cached.AtUtc).TotalSeconds < 5)
        {
            await ApplyStorageStatsOnUiAsync(cached.Stats, emptySelection: connectionId <= 0).ConfigureAwait(true);
            return;
        }

        if (SelectedConnection is not { Id: > 0 } connection)
        {
            var fileStats = await _trendSampleRepository.GetDatabaseFileStatsAsync(0).ConfigureAwait(false);
            _lastStorageStats = (fileStats, DateTimeOffset.UtcNow, 0);
            await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
            {
                DatabaseFileSizeText = StorageSizeFormatter.FormatBytes(fileStats.TotalFileBytes);
                ConnectionHistoryStorageText = "请选择连接档案";
                ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
            }).ConfigureAwait(true);
            return;
        }

        try
        {
            var stats = await _trendSampleRepository.GetDatabaseFileStatsAsync(connection.Id).ConfigureAwait(false);
            _lastStorageStats = (stats, DateTimeOffset.UtcNow, connection.Id);
            await ApplyStorageStatsOnUiAsync(stats, emptySelection: false).ConfigureAwait(true);
        }
        catch
        {
            await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
            {
                DatabaseFileSizeText = "—";
                ConnectionHistoryStorageText = "无法读取";
                ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
            }).ConfigureAwait(true);
        }
    }

    private Task ApplyStorageStatsOnUiAsync(DatabaseFileStats stats, bool emptySelection) =>
        AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            DatabaseFileSizeText = StorageSizeFormatter.FormatBytes(stats.TotalFileBytes);
            ConnectionHistoryStorageText = emptySelection
                ? "请选择连接档案"
                : $"{stats.ConnectionSamples.SampleCount} 条采样 · 约 {StorageSizeFormatter.FormatBytes(stats.ConnectionSamples.EstimatedBytes)}";
            ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
        });

    private async Task LoadAutoFallbackModeAsync()
    {
        var text = await _appSettings.GetAsync(AppConstants.AutoSecurityFallbackKey).ConfigureAwait(false);
        var mode = AutoSecurityFallbackHelper.ParseOrDefault(text);
        var option = AutoFallbackModeOptions.FirstOrDefault(item => item.Mode == mode)
                     ?? AutoFallbackModeOptions[0];
        await AppUiDispatcher.InvokeSafeAwaitAsync(() => SelectedAutoFallbackModeOption = option)
            .ConfigureAwait(true);
    }

    private async Task LoadPollIntervalAsync()
    {
        var text = await _appSettings.GetAsync(AppConstants.PollIntervalMsKey).ConfigureAwait(false);
        var interval = PollIntervalHelper.ParseOrDefault(text);
        await AppUiDispatcher.InvokeSafeAwaitAsync(() => PollIntervalMs = interval).ConfigureAwait(true);
    }

    private async Task ApplyConnectionToFormAsync(PlcConnectionEntity connection)
    {
        var loadSerial = ++_formLoadSerial;
        var timeout = connection.TimeoutSeconds <= 0 ? 10 : connection.TimeoutSeconds;
        var securityMode = string.IsNullOrWhiteSpace(connection.SecurityMode) ? "Auto" : connection.SecurityMode;
        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            ConnectionName = connection.Name;
            Host = connection.Host;
            Port = connection.Port;
            TimeoutSeconds = timeout;
            Username = connection.Username ?? string.Empty;
            SecurityMode = securityMode;
            IsDefaultConnection = connection.IsDefault;
            IsPasswordVisible = false;
            _session.UpdateConnectionEndpoint(Host, Port);
        }).ConfigureAwait(true);

        var password = connection.Id > 0
            ? await _connectionRepository.GetPasswordAsync(connection.Id).ConfigureAwait(false) ?? string.Empty
            : string.Empty;
        if (loadSerial != _formLoadSerial)
        {
            return;
        }

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            Password = password;
            NotifySessionDerivedProperties();
        }).ConfigureAwait(true);
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPasswordMasked));
        OnPropertyChanged(nameof(PasswordVisibilityToggleText));
        OnPropertyChanged(nameof(PasswordVisibilityToggleHint));
    }

    [RelayCommand]
    private void TogglePasswordMask() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private async Task SaveConnectionAsync()
    {
        if (IsConnectionLocked)
        {
            _session.StatusMessage = "已连接 PLC，请先断开再保存档案。";
            return;
        }

        _session.IsBusy = true;
        _session.StatusMessage = "正在保存连接档案...";

        try
        {
            await PersistConnectionAsync(showSavedMessage: true).ConfigureAwait(true);
        }
        finally
        {
            _session.IsBusy = false;
            NotifySessionDerivedProperties();
        }
    }

    [RelayCommand]
    private async Task NewConnectionAsync()
    {
        if (IsConnectionLocked)
        {
            _session.StatusMessage = "已连接 PLC，请先断开再新建连接档案。";
            return;
        }

        try
        {
            // WinUI Picker 不能往 ItemsSource 插入/空选，新建只改表单，保存后再刷新列表。
            EnterCreateMode();
            _formLoadSerial++;
            ConnectionName = ConnectionNameHelper.GenerateUniqueName(Connections.Select(item => item.Name));
            Host = "192.168.0.1";
            Port = 102;
            TimeoutSeconds = 10;
            Username = string.Empty;
            Password = string.Empty;
            IsPasswordVisible = false;
            SecurityMode = "Auto";
            IsDefaultConnection = Connections.Count == 0;
            _session.UpdateConnectionEndpoint(Host, Port);
            _session.StatusMessage = "正在编辑新连接档案";
            NotifySessionDerivedProperties();
            await RefreshStorageStatsAsync(force: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ExitCreateMode();
            await ShowErrorAlertAsync("新建失败", ExceptionFormatter.Format(ex)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteConnectionAsync()
    {
        if (_isCreatingNewProfile)
        {
            ExitCreateMode();
            if (SelectedConnection != null)
            {
                await ApplyConnectionToFormAsync(SelectedConnection).ConfigureAwait(true);
            }

            _session.StatusMessage = "已取消新建档案";
            await RefreshStorageStatsAsync(force: true).ConfigureAwait(true);
            return;
        }

        if (SelectedConnection == null)
        {
            _session.StatusMessage = "请先选择要删除的连接档案。";
            return;
        }

        try
        {
            var deletedId = SelectedConnection.Id;
            var deletedName = SelectedConnection.Name;
            var itemCount = await _monitorSheetRepository
                .CountMonitorItemsByConnectionAsync(deletedId)
                .ConfigureAwait(true);

            var message = itemCount == 0
                ? $"将永久删除连接档案「{deletedName}」及其已保存密码，并清除该档案的全部历史采样。\n\n确定删除？"
                : $"将永久删除连接档案「{deletedName}」及其已保存密码，并删除 {itemCount} 个监视变量、趋势配置与全部历史采样。\n\n确定删除？";

            var confirmed = await UiDialog.ConfirmAsync(
                $"删除连接「{deletedName}」？",
                message,
                "删除",
                "取消").ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }

            if (IsConnected)
            {
                await _session.DisconnectAsync().ConfigureAwait(true);
            }

            var nextId = Connections
                .Where(item => item.Id > 0 && item.Id != deletedId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .FirstOrDefault()
                ?.Id;
            await _connectionRepository.DeleteAsync(deletedId).ConfigureAwait(true);

            await WithSuppressedSelectionAsync(async () =>
            {
                var next = await _session.RefreshConnectionsAndSelectAsync(
                        nextId,
                        item => SelectedConnection = item)
                    .ConfigureAwait(true);
                if (next != null)
                {
                    await ApplyConnectionToFormAsync(next).ConfigureAwait(true);
                }
            }).ConfigureAwait(true);

            await _session.ApplySelectedConnectionAsync(SelectedConnection).ConfigureAwait(true);
            _session.NotifyMonitorSheetChanged();
            _session.StatusMessage = "连接档案已删除";
            await RefreshStorageStatsAsync(force: true).ConfigureAwait(true);
            NotifySessionDerivedProperties();
        }
        catch (Exception ex)
        {
            await ShowErrorAlertAsync("删除失败", $"删除档案失败：{ExceptionFormatter.Format(ex)}")
                .ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearConnectionHistory))]
    private async Task ClearConnectionHistoryAsync()
    {
        if (SelectedConnection is not { Id: > 0 } connection)
        {
            _session.StatusMessage = "请先选择连接档案。";
            return;
        }

        var stats = await _trendSampleRepository.GetDatabaseFileStatsAsync(connection.Id).ConfigureAwait(true);
        if (stats.ConnectionSamples.SampleCount == 0)
        {
            _session.StatusMessage = "当前档案没有可清理的历史采样。";
            return;
        }

        var confirmed = await UiDialog.ConfirmAsync(
            $"清理「{connection.Name}」历史采样？",
            $"将删除 {stats.ConnectionSamples.SampleCount} 条 trend_samples 记录（约 {StorageSizeFormatter.FormatBytes(stats.ConnectionSamples.EstimatedBytes)}），不影响监视变量与趋势配置。\n\n删除监视行 / 从趋势图移除变量仅停止采样，不会自动删历史；请在此手动清理。",
            "清理",
            "取消").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        _session.IsBusy = true;
        try
        {
            await _trendSampleRepository.ClearTrendSamplesByConnectionAsync(connection.Id).ConfigureAwait(true);
            _session.NotifyTrendHistoryCleared(connection.Id);
            await RefreshStorageStatsAsync(force: true).ConfigureAwait(true);
            _session.StatusMessage = $"已清理「{connection.Name}」的历史采样。";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"清理失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            _session.IsBusy = false;
            NotifySessionDerivedProperties();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        _session.IsBusy = true;
        NotifySessionDerivedProperties();
        try
        {
            if (!await PersistConnectionAsync(showSavedMessage: false).ConfigureAwait(true))
            {
                return;
            }
        }
        finally
        {
            _session.IsBusy = false;
            NotifySessionDerivedProperties();
        }

        if (SelectedConnection is not { Id: > 0 })
        {
            _session.StatusMessage = "请先保存并选择连接档案，再执行连接。";
            return;
        }

        var settings = BuildConnectionSettings();
        await _session.ConnectAsync(settings).ConfigureAwait(true);
        if (_session.IsConnected)
        {
            await _browseViewModel.BrowseVariablesAsync().ConfigureAwait(true);
        }

        NotifySessionDerivedProperties();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await _session.DisconnectAsync().ConfigureAwait(true);
        NotifySessionDerivedProperties();
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAndGoAsync()
    {
        await _browseViewModel.BrowseVariablesAsync().ConfigureAwait(true);
        await AppNavigation.GoToAsync("browse").ConfigureAwait(true);
    }

    private PlcConnectionSettings BuildConnectionSettings() =>
        new()
        {
            Host = Host.Trim(),
            Port = Port,
            TimeoutSeconds = TimeoutSeconds,
            Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
            Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
            SecurityMode = PlcConnectionMapper.ParseSecurityMode(SecurityMode),
            AutoFallbackMode = SelectedAutoFallbackModeOption?.Mode ?? AutoSecurityFallbackMode.Driver
        };

    private static void ApplyStoredMetadata(PlcConnectionEntity target, PlcConnectionEntity stored)
    {
        target.Id = stored.Id;
        target.CreatedAt = stored.CreatedAt;
        target.LastUsedAt = stored.LastUsedAt;
        target.SortOrder = stored.SortOrder;
    }

    private async Task<bool> PersistConnectionAsync(bool showSavedMessage)
    {
        var trimmedName = ConnectionName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            await ShowSaveFailureAsync("请输入档案名称。").ConfigureAwait(true);
            return false;
        }

        ConnectionName = trimmedName;

        var trimmedHost = Host.Trim();
        if (string.IsNullOrWhiteSpace(trimmedHost))
        {
            await ShowSaveFailureAsync("请输入 IP 或主机名。").ConfigureAwait(true);
            return false;
        }

        Host = trimmedHost;

        if (!PlcConnectionSavePlanner.IsValidPort(Port))
        {
            await ShowSaveFailureAsync(
                    $"端口请填写 {PlcConnectionSavePlanner.MinPort}–{PlcConnectionSavePlanner.MaxPort}。")
                .ConfigureAwait(true);
            return false;
        }

        if (!PlcConnectionSavePlanner.IsValidTimeoutSeconds(TimeoutSeconds))
        {
            await ShowSaveFailureAsync("超时请填写大于 0 的秒数（不是毫秒）。").ConfigureAwait(true);
            return false;
        }

        if (!PollIntervalHelper.IsValid(PollIntervalMs))
        {
            await ShowSaveFailureAsync(
                    $"监视周期无效，请输入 {AppConstants.MinPollIntervalMs}–{AppConstants.MaxPollIntervalMs} ms。")
                .ConfigureAwait(true);
            return false;
        }

        try
        {
            var nowEntity = BuildEntityFromForm();
            var preferredId = _isCreatingNewProfile
                ? null
                : SelectedConnection is { Id: > 0 } selected ? selected.Id : (int?)null;
            var existingByName = await _connectionRepository.FindByNameAsync(trimmedName).ConfigureAwait(true);
            var plan = PlcConnectionSavePlanner.Plan(preferredId, existingByName?.Id, existingByName?.Name);
            if (plan.Action == PlcConnectionSaveAction.Reject)
            {
                await ShowSaveFailureAsync(plan.ErrorMessage ?? "名称已被使用。").ConfigureAwait(true);
                return false;
            }

            if (plan.Action == PlcConnectionSaveAction.Update && plan.UpdateId is int updateId)
            {
                var selectedEntity = await _connectionRepository.GetAsync(updateId).ConfigureAwait(true);
                if (selectedEntity == null)
                {
                    var id = await _connectionRepository.CreateAsync(nowEntity).ConfigureAwait(true);
                    nowEntity.Id = id;
                }
                else
                {
                    ApplyStoredMetadata(nowEntity, selectedEntity);
                    await _connectionRepository.UpdateAsync(nowEntity).ConfigureAwait(true);
                }
            }
            else
            {
                var id = await _connectionRepository.CreateAsync(nowEntity).ConfigureAwait(true);
                nowEntity.Id = id;
            }

            if (!string.IsNullOrWhiteSpace(Password))
            {
                await _connectionRepository.SavePasswordAsync(nowEntity.Id, Password).ConfigureAwait(true);
            }
            else
            {
                await _connectionRepository.ClearPasswordAsync(nowEntity.Id).ConfigureAwait(true);
            }

            await SyncSavedConnectionAsync(nowEntity.Id).ConfigureAwait(true);
            if (!await SaveGlobalSettingsAsync(showReconnectHint: false).ConfigureAwait(true))
            {
                await ShowSaveFailureAsync(
                        $"监视周期无效，请输入 {AppConstants.MinPollIntervalMs}–{AppConstants.MaxPollIntervalMs} ms。")
                    .ConfigureAwait(true);
                return false;
            }

            if (showSavedMessage)
            {
                await AppUiDispatcher.InvokeSafeAwaitAsync(
                        () => _session.StatusMessage = $"已保存连接档案：{nowEntity.Name}")
                    .ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            await ShowSaveFailureAsync(ExceptionFormatter.Format(ex)).ConfigureAwait(true);
            return false;
        }
    }

    private Task ShowSaveFailureAsync(string detail)
    {
        var message = detail.StartsWith("保存失败", StringComparison.Ordinal)
            ? detail
            : $"保存失败：{detail}";
        return ShowErrorAlertAsync("保存失败", message);
    }

    private async Task ShowErrorAlertAsync(string title, string message)
    {
        _session.StatusMessage = message;
        await UiDialog.AlertAsync(title, message).ConfigureAwait(true);
    }

    private async Task SyncSavedConnectionAsync(int connectionId)
    {
        await WithSuppressedSelectionAsync(async () =>
        {
            var saved = await _session
                .RefreshConnectionsAndSelectAsync(connectionId, item => SelectedConnection = item)
                .ConfigureAwait(true);
            ExitCreateMode();
            if (saved != null)
            {
                SelectedConnection = saved;
            }
        }).ConfigureAwait(true);
    }

    private void EnterCreateMode()
    {
        _isCreatingNewProfile = true;
        OnPropertyChanged(nameof(IsCreatingNewProfile));
        OnPropertyChanged(nameof(NewProfileHint));
        OnPropertyChanged(nameof(CanClearConnectionHistory));
        ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
    }

    private void ExitCreateMode()
    {
        if (!_isCreatingNewProfile)
        {
            return;
        }

        _isCreatingNewProfile = false;
        OnPropertyChanged(nameof(IsCreatingNewProfile));
        OnPropertyChanged(nameof(NewProfileHint));
        OnPropertyChanged(nameof(CanClearConnectionHistory));
        ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
    }

    private async Task WithSuppressedSelectionAsync(Func<Task> action)
    {
        _isConnectionSelectionHandlingSuppressed = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            _isConnectionSelectionHandlingSuppressed = false;
        }
    }

    private PlcConnectionEntity BuildEntityFromForm() =>
        new()
        {
            Name = ConnectionName.Trim(),
            Host = Host.Trim(),
            Port = Port,
            TimeoutSeconds = TimeoutSeconds,
            Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
            SecurityMode = SecurityMode,
            IsDefault = IsDefaultConnection,
            SortOrder = _isCreatingNewProfile
                ? Connections.Count
                : SelectedConnection?.SortOrder ?? Connections.Count
        };

    private void NotifySessionDerivedProperties()
    {
        AppUiDispatcher.InvokeSafe(() =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsConnectionLocked));
            OnPropertyChanged(nameof(CanEditConnectionProfile));
            OnPropertyChanged(nameof(ConnectionLockHint));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            OnPropertyChanged(nameof(CanSaveConnection));
            OnPropertyChanged(nameof(CanBrowse));
            OnPropertyChanged(nameof(CanClearConnectionHistory));
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            BrowseAndGoCommand.NotifyCanExecuteChanged();
            SaveConnectionCommand.NotifyCanExecuteChanged();
            NewConnectionCommand.NotifyCanExecuteChanged();
            ClearConnectionHistoryCommand.NotifyCanExecuteChanged();
        });
    }

    private async Task<bool> SaveGlobalSettingsAsync(bool showReconnectHint)
    {
        if (!PollIntervalHelper.IsValid(PollIntervalMs))
        {
            return false;
        }

        await _appSettings
            .SetAsync(AppConstants.PollIntervalMsKey, PollIntervalMs.ToString())
            .ConfigureAwait(true);

        var syncedSampleIntervalMs = TrendRangeHelper.SampleIntervalFromPollInterval(PollIntervalMs);
        await _trendViewRepository
            .SetAllSampleIntervalMsAsync(syncedSampleIntervalMs)
            .ConfigureAwait(true);
        _session.NotifyTrendViewSettingsChanged();

        var fallbackMode = SelectedAutoFallbackModeOption?.Mode ?? AutoSecurityFallbackMode.Driver;
        await _appSettings
            .SetAsync(
                AppConstants.AutoSecurityFallbackKey,
                AutoSecurityFallbackHelper.ToStorageValue(fallbackMode))
            .ConfigureAwait(true);

        if (showReconnectHint && _session.IsConnected)
        {
            await AppUiDispatcher.InvokeSafeAwaitAsync(
                    () => _session.StatusMessage = "全局设置已保存，重新连接后生效。")
                .ConfigureAwait(true);
        }

        return true;
    }
}
