using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>监视表页 VM：订阅轮询结果并映射到行，处理强制写入与趋势加入。</summary>

public partial class MonitorPageViewModel : ObservableObject, IAppShutdownParticipant
{
    private readonly AppSessionViewModel _session;
    private readonly IMonitorSheetRepository _monitorSheetRepository;
    private readonly IPlcService _plcService;
    private readonly PollingMonitorEngine _pollingEngine;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private int? _activeConnectionId;
    private PollingCycleCompletedEventArgs? _cachedPollCycleResult;

    public MonitorPageViewModel(
        AppSessionViewModel session,
        IMonitorSheetRepository monitorSheetRepository,
        TrendWorkspaceViewModel trendWorkspace,
        IPlcService plcService,
        PollingMonitorEngine pollingEngine)
    {
        _session = session;
        _monitorSheetRepository = monitorSheetRepository;
        _plcService = plcService;
        _pollingEngine = pollingEngine;
        TrendWorkspace = trendWorkspace;
        IsForceColumnVisible = true;
        _session.SelectedConnectionApplied += OnSessionSelectedConnectionApplied;
        // A2 解耦：监视表变更经 WeakReferenceMessenger 单一订阅，替代 C# 事件
        WeakReferenceMessenger.Default.Register<MonitorSheetChangedMessage>(
            this, static (recipient, _) => ((MonitorPageViewModel)recipient).OnMonitorSheetChanged());
        _session.TrendMembershipChanged += OnSessionTrendMembershipChanged;
        _session.PropertyChanged += OnSessionPropertyChanged;
        _pollingEngine.CycleCompleted += OnPollingCycleCompleted;
    }

    public void PrepareForShutdown()
    {
        _pollingEngine.CycleCompleted -= OnPollingCycleCompleted;
        WeakReferenceMessenger.Default.Unregister<MonitorSheetChangedMessage>(this);
        _session.SelectedConnectionApplied -= OnSessionSelectedConnectionApplied;
        _session.TrendMembershipChanged -= OnSessionTrendMembershipChanged;
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }

    private void OnSessionSelectedConnectionApplied(object? sender, EventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = ReloadAsync();
    }

    private void OnMonitorSheetChanged()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = ReloadAsync();
    }

    private void OnSessionTrendMembershipChanged(object? sender, EventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = RefreshTrendMembershipAsync();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.IsConnectionLocked)
            or nameof(AppSessionViewModel.ConnectedConnectionId)
            or nameof(AppSessionViewModel.IsBusy))
        {
            NotifySessionStateChanged();
            if (e.PropertyName is nameof(AppSessionViewModel.IsBusy)
                && !_session.IsBusy
                && _session.IsConnectionLocked)
            {
                _ = _session.EnsureMonitoringAsync();
                _ = ReloadAsync();
            }
            else if (e.PropertyName is not nameof(AppSessionViewModel.IsBusy))
            {
                _ = ReloadAsync();
            }
        }
        else if (e.PropertyName is nameof(AppSessionViewModel.ActiveConnectionProfileName))
        {
            NotifySessionStateChanged();
        }
        else if (e.PropertyName is nameof(AppSessionViewModel.CanStartMonitoring)
                 or nameof(AppSessionViewModel.CanStopMonitoring)
                 or nameof(AppSessionViewModel.IsMonitoringActive))
        {
            NotifyMonitoringCommandsChanged();
        }
    }

    private void OnPollingCycleCompleted(object? sender, PollingCycleCompletedEventArgs args) =>
        ApplyReadResults(args);

    public TrendWorkspaceViewModel TrendWorkspace { get; }

    public bool IsForceColumnVisible { get; }

    [ObservableProperty]
    private bool _isMonitorPanelExpanded = true;

    public int ItemCount => Items.Count;

    public bool HasActiveSession => _session.HasActiveSession;

    public string PanelTitle => HasActiveSession
        ? $"监视表 · {_session.ActiveConnectionProfileName}"
        : "监视表";

    public string EmptyHint => !HasActiveSession
        ? "🔌 请先连接 PLC"
        : "➕ 在浏览 Tab 添加变量到监视表";

    public string ItemCountText => HasActiveSession
        ? $"共 {ItemCount} 个监视变量"
        : string.Empty;

    public bool CanStartMonitoring => _session.CanStartMonitoring;

    public bool CanStopMonitoring => _session.CanStopMonitoring;

    public ObservableCollection<MonitorSheetItemViewModel> Items { get; } = [];

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(ItemCountText));
        NotifyMonitoringCommandsChanged();
    }

    private void NotifyMonitoringCommandsChanged()
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        StartMonitorCommand.NotifyCanExecuteChanged();
        StopMonitorCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
    private Task StartMonitorAsync() => _session.StartMonitoringAsync();

    [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
    private Task StopMonitorAsync() => _session.StopMonitoringManuallyAsync();

    [RelayCommand]
    private void ToggleMonitorPanel()
    {
        IsMonitorPanelExpanded = !IsMonitorPanelExpanded;
    }

    public async Task InitializeAsync()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        try
        {
            await _session.EnsureMonitoringAsync().ConfigureAwait(false);

            var connectionId = _session.ConnectedConnectionId;
            if (_activeConnectionId != connectionId)
            {
                await ReloadAsync().ConfigureAwait(false);
            }
            else
            {
                NotifySessionStateChanged();
                await AppUiDispatcher.InvokeSafeAwaitAsync(ApplyCachedPollResultsOnCurrentThread).ConfigureAwait(false);
            }

            if (!_session.IsShuttingDown)
            {
                await TrendWorkspace.ReloadAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = $"监视页初始化失败：{ExceptionFormatter.Format(ex)}";
            }
        }
    }

    public async Task ReloadAsync()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        await _reloadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            if (!_session.IsConnected)
            {
                _activeConnectionId = null;
                _cachedPollCycleResult = null;
                await ApplyItemsOnMainThreadAsync([], new HashSet<int>()).ConfigureAwait(false);
                return;
            }

            if (_session.ConnectedConnectionId is not int connectionId || connectionId <= 0)
            {
                _activeConnectionId = null;
                await ApplyItemsOnMainThreadAsync([], new HashSet<int>()).ConfigureAwait(false);
                return;
            }

            var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(connectionId).ConfigureAwait(false);
            var entities = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(false);
            var trendItemIds = await TrendWorkspace.GetTrendItemIdsAsync().ConfigureAwait(false);
            _activeConnectionId = connectionId;
            await ApplyItemsOnMainThreadAsync(entities, trendItemIds).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task ApplyItemsOnMainThreadAsync(
        IReadOnlyList<MonitorSheetItemEntity> entities,
        IReadOnlySet<int> trendItemIds)
    {
        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            Items.Clear();
            for (var i = 0; i < entities.Count; i++)
            {
                Items.Add(CreateRow(entities[i], i + 1, trendItemIds.Contains(entities[i].Id)));
            }

            OnPropertyChanged(nameof(ItemCount));
            NotifySessionStateChanged();
            ApplyCachedPollResultsOnCurrentThread();
        }).ConfigureAwait(false);
    }

    private async Task RefreshTrendMembershipAsync()
    {
        if (_session.IsShuttingDown || !HasActiveSession)
        {
            return;
        }

        var trendItemIds = await TrendWorkspace.GetTrendItemIdsAsync().ConfigureAwait(false);
        if (_session.IsShuttingDown)
        {
            return;
        }

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            foreach (var row in Items)
            {
                row.IsInTrend = trendItemIds.Contains(row.ItemId);
            }
        }).ConfigureAwait(false);
    }

    private MonitorSheetItemViewModel CreateRow(MonitorSheetItemEntity entity, int index, bool isInTrend)
    {
        MonitorSheetItemViewModel? row = null;
        row = new MonitorSheetItemViewModel(entity)
        {
            Index = index,
            IsInTrend = isInTrend,
            IsSymbolMissing = _session.IsSymbolMissingFromBrowse(entity.Symbol),
            ToggleTrendHandler = () => TrendWorkspace.ToggleItemTrendAsync(row!),
            DisplayFormatChangedHandler = formatId => PersistDisplayFormatAsync(entity.Id, formatId),
            ForceWriteHandler = () => ForceWriteAsync(row!),
            ToggleBoolHandler = () => ToggleBoolAsync(row!),
            RemoveHandler = () => RemoveItemAsync(row!)
        };
        return row;
    }

    private async Task PersistDisplayFormatAsync(int itemId, string formatId)
    {
        try
        {
            await _monitorSheetRepository.UpdateItemDisplayFormatAsync(itemId, formatId).ConfigureAwait(false);
            await AppUiDispatcher.InvokeSafeAwaitAsync(
                    () => _session.StatusMessage = "显示格式已保存（趋势中变量需暂停后重载历史以同步新格式）")
                .ConfigureAwait(true);
            // B8：显示格式变更若影响趋势中变量的数值展示，通知趋势工作区在下次暂停时重载
            _session.NotifyTrendViewSettingsChanged();
        }
        catch (Exception ex)
        {
            await AppUiDispatcher.InvokeSafeAwaitAsync(
                    () => _session.StatusMessage = $"保存显示格式失败：{ExceptionFormatter.Format(ex)}")
                .ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveItemAsync(MonitorSheetItemViewModel row)
    {
        if (!HasActiveSession)
        {
            _session.StatusMessage = "请先连接 PLC。";
            return;
        }

        var confirmed = await UiDialog.ConfirmAsync(
            "删除监视行？",
            $"将从监视表及趋势图中移除「{row.Symbol}」，并删除该变量的全部历史采样。",
            "删除",
            "取消").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            // 竞态修复：先同步摘除 TrendWorkspace 内存成员，
            // 使在途/后续轮询不再携带该 item_id 写 trend_samples（否则 FK 约束失败整批回滚）
            TrendWorkspace.DetachItemFromMemory(row.ItemId);
            await _monitorSheetRepository.RemoveItemAsync(row.ItemId).ConfigureAwait(true);
            _session.NotifyMonitorSheetChanged();
            _session.NotifyTrendMembershipChanged();
            await TrendWorkspace.ReloadAsync().ConfigureAwait(true);
            _session.StatusMessage = "监视行已删除";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"删除监视行失败：{ExceptionFormatter.Format(ex)}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _activeConnectionId = null;
        await ReloadAsync().ConfigureAwait(false);
        if (_session.IsShuttingDown)
        {
            return;
        }

        await TrendWorkspace.ReloadAsync().ConfigureAwait(false);
        if (!_session.IsShuttingDown)
        {
            _session.StatusMessage = HasActiveSession ? "监视表已刷新" : "未连接 PLC，监视表为空";
        }
    }

    private void ApplyReadResults(PollingCycleCompletedEventArgs args)
    {
        if (_session.IsShuttingDown || !_session.IsConnectionLocked)
        {
            return;
        }

        // 大监视表（>2000）不长期持有 50k Dictionary，避免 4GB 常驻；直接应用当前帧
        if (args.Results.Count > 2000)
        {
            var snapshot = args.Results;
            AppUiDispatcher.InvokeSafeAsync(() =>
            {
                if (_session.IsShuttingDown || Items.Count == 0) return;
                foreach (var row in Items)
                {
                    if (snapshot.TryGetValue(row.Symbol, out var result))
                        row.ApplyReadResult(result);
                }
            });
            return;
        }

        if (args.Results.Count > 0)
        {
            _cachedPollCycleResult = args;
        }

        AppUiDispatcher.InvokeSafeAsync(ApplyCachedPollResultsOnCurrentThread);
    }

    private void ApplyCachedPollResultsOnCurrentThread()
    {
        if (_session.IsShuttingDown
            || _cachedPollCycleResult == null
            || _cachedPollCycleResult.Results.Count == 0
            || Items.Count == 0)
        {
            return;
        }

        var results = _cachedPollCycleResult.Results;
        foreach (var row in Items)
        {
            if (results.TryGetValue(row.Symbol, out var result))
            {
                row.ApplyReadResult(result);
            }
        }
    }

    private async Task ForceWriteAsync(MonitorSheetItemViewModel row)
    {
        if (!_session.IsConnected)
        {
            _session.StatusMessage = "未连接 PLC，无法写入。";
            return;
        }

        var parseResult = ForceValueParser.Parse(row.ForceValueInput, row.DisplayFormat, row.DataType);
        if (!parseResult.IsSuccess)
        {
            _session.StatusMessage = parseResult.ErrorMessage ?? "强制值与显示格式不匹配。";
            return;
        }

        var displayValue = ValueDisplayFormatter.Format(parseResult.Value, row.DisplayFormat, row.DataType);
        var confirmed = await UiDialog.ConfirmAsync(
            "确认强制写入？",
            $"将向 PLC 写入「{row.Symbol}」= {displayValue}。\n\n强制写入可能影响设备运行，请确认安全。",
            "写入",
            "取消").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            _session.SuspendMonitoring();
            var result = await _plcService.WriteVariableAsync(new VariableWriteRequest
            {
                Symbol = row.Symbol,
                Value = parseResult.Value!
            }).ConfigureAwait(true);

            _session.StatusMessage = result.IsSuccess
                ? $"已写入 {row.Symbol}"
                : $"写入失败：{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"写入失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            await _session.ResumeMonitoringAsync().ConfigureAwait(true);
        }
    }

    private async Task ToggleBoolAsync(MonitorSheetItemViewModel row)
    {
        if (!_session.IsConnected)
        {
            _session.StatusMessage = "未连接 PLC，无法写入。";
            return;
        }

        if (row.RawValue is not bool currentValue)
        {
            _session.StatusMessage = "当前值无效，无法反转。";
            return;
        }

        var nextValue = !currentValue;
        var confirmed = await UiDialog.ConfirmAsync(
            "确认写入/反转？",
            $"将向 PLC 写入「{row.Symbol}」= {(nextValue ? "TRUE" : "FALSE")}。\n\n强制写入可能影响设备运行，请确认安全。",
            "写入/反转",
            "取消").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            _session.SuspendMonitoring();
            var result = await _plcService.ToggleBoolAsync(row.Symbol, currentValue).ConfigureAwait(true);
            _session.StatusMessage = result.IsSuccess
                ? $"已反转 {row.Symbol}"
                : $"写入失败：{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"写入失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            await _session.ResumeMonitoringAsync().ConfigureAwait(true);
        }
    }
}
