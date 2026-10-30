using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.Services;

namespace S7PlusMonitor.ViewModels;

/// <summary>单连接单张默认趋势图工作区。</summary>
public partial class TrendWorkspaceViewModel : ObservableObject, IAppShutdownParticipant
{
    private readonly AppSessionViewModel _session;
    private readonly ITrendViewRepository _trendViewRepository;
    private readonly IMonitorSheetRepository _monitorSheetRepository;
    private readonly ITrendSampleRepository _trendSampleRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly PollingMonitorEngine _pollingEngine;
    private readonly TrendHistorySampleCache _sampleCache = new();
    private readonly TrendLiveSampleBuffer _liveBuffer = new();
    private readonly Dictionary<string, int> _symbolToMonitorItemIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MonitorSheetItemEntity> _monitorItemById = new();
    private readonly HashSet<int> _trendedMonitorItemIds = [];
    /// <summary>成员映射同步锁：轮询线程读 / UI 线程写（增删行、重载），一律经此锁或快照访问。</summary>
    private readonly object _membershipSync = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly SemaphoreSlim _chartUpdateLock = new(1, 1);
    private readonly SemaphoreSlim _membershipGate = new(1, 1);
    private TrendViewEntity? _viewEntity;
    private int _connectionId;
    private int _defaultViewId;
    private DateTimeOffset _lastPersistedSampleUtc = DateTimeOffset.MinValue;
    /// <summary>实时图表有待刷新（合并刷新，避免轮询 await UI）。仅经 Volatile/Interlocked 访问，屏障已由其提供，故不加 volatile（CS0420）。</summary>
    private int _liveRefreshPendingFlag;
    /// <summary>历史 SQLite 分页加载取消源。</summary>
    private CancellationTokenSource? _historyLoadingCts;
    /// <summary>是否已有 <see cref="EnsureLiveChartRefreshAsync"/> 循环在运行。仅经 Interlocked 访问，同上不加 volatile。</summary>
    private int _liveChartRefreshLoopRunning;

    public TrendWorkspaceViewModel(
        AppSessionViewModel session,
        ITrendViewRepository trendViewRepository,
        IMonitorSheetRepository monitorSheetRepository,
        ITrendSampleRepository trendSampleRepository,
        IAppSettingsRepository appSettings,
        PollingMonitorEngine pollingEngine)
    {
        _session = session;
        _trendViewRepository = trendViewRepository;
        _monitorSheetRepository = monitorSheetRepository;
        _trendSampleRepository = trendSampleRepository;
        _appSettings = appSettings;
        _pollingEngine = pollingEngine;
        // A2 解耦：监视表变更经 WeakReferenceMessenger 单一订阅，替代 C# 事件
        WeakReferenceMessenger.Default.Register<MonitorSheetChangedMessage>(
            this, static (recipient, _) => ((TrendWorkspaceViewModel)recipient).OnMonitorSheetChanged());
        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.TrendHistoryCleared += OnTrendHistoryCleared;
        _session.TrendViewSettingsChanged += OnTrendViewSettingsChanged;
        _pollingEngine.CycleCompleted += OnPollingCycleCompleted;
        _ = LoadExpandedStateAsync();
    }

    public void PrepareForShutdown()
    {
        CancelHistoryLoad();
        _pollingEngine.CycleCompleted -= OnPollingCycleCompleted;
        WeakReferenceMessenger.Default.Unregister<MonitorSheetChangedMessage>(this);
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.TrendHistoryCleared -= OnTrendHistoryCleared;
        _session.TrendViewSettingsChanged -= OnTrendViewSettingsChanged;
    }

    private void OnTrendViewSettingsChanged(object? sender, EventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = ReloadTrendViewSettingsAsync();
    }

    private async Task ReloadTrendViewSettingsAsync()
    {
        if (_session.IsShuttingDown || _defaultViewId <= 0)
        {
            return;
        }

        var entity = await _trendViewRepository.GetViewAsync(_defaultViewId).ConfigureAwait(false);
        if (entity == null || _session.IsShuttingDown)
        {
            return;
        }

        _viewEntity = entity;
        _lastPersistedSampleUtc = DateTimeOffset.MinValue;
        _sampleCache.Clear();
        await ApplyChartOnMainThreadAsync(entity).ConfigureAwait(false);
    }

    private void OnTrendHistoryCleared(object? sender, int connectionId)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _lastPersistedSampleUtc = DateTimeOffset.MinValue;
        _sampleCache.Clear();
        _liveBuffer.Clear();
        var relevant = _connectionId == connectionId
            || _session.ConnectedConnectionId == connectionId
            || _session.SelectedConnection?.Id == connectionId;
        if (relevant)
        {
            _ = ReloadAsync();
        }
    }

    private void OnMonitorSheetChanged()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = ReloadMonitorMapAndChartAsync();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.IsConnectionLocked)
            or nameof(AppSessionViewModel.ConnectedConnectionId)
            or nameof(AppSessionViewModel.HasActiveSession)
            or nameof(AppSessionViewModel.IsConnected))
        {
            NotifySessionStateChanged();
            _ = ReloadAsync();
        }
        else if (e.PropertyName is nameof(AppSessionViewModel.ActiveConnectionProfileName))
        {
            NotifySessionStateChanged();
        }
    }

    private void OnPollingCycleCompleted(object? sender, PollingCycleCompletedEventArgs args)
    {
        if (_session.IsShuttingDown || !HasActiveSession || args.Results.Count == 0 || _symbolToMonitorItemIdMap.Count == 0)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;

        if (_trendedMonitorItemIds.Count == 0 || !args.IsSuccess)
        {
            return;
        }

        if (!IsChartPaused)
        {
            AppendPollResultsToLiveBuffer(args, timestamp);
            RequestLiveChartRefresh();
        }

        _ = TryPersistSamplesAsync(args, timestamp);
    }

    [ObservableProperty]
    private TrendViewViewModel? _activeView;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isChartPaused;

    public bool HasActiveSession => _session.HasActiveSession;

    public bool HasNoActiveSession => !HasActiveSession;

    public bool CanUseTrendTools => HasActiveSession && ActiveView != null;

    public string PanelTitle => HasActiveSession
        ? $"趋势图 · {_session.ActiveConnectionProfileName}"
        : "趋势图";

    public string EmptyHint => "🔌 请先连接 PLC";

    public string PauseButtonText => IsChartPaused ? "▶ 继续" : "⏸ 暂停";

    partial void OnIsExpandedChanged(bool value)
    {
        _ = SaveExpandedStateAsync(value);
        if (value && HasActiveSession)
        {
            _ = EnsureChartVisibleAsync();
        }
    }

    partial void OnIsChartPausedChanged(bool value)
    {
        ActiveView?.SetChartPaused(value);
        OnPropertyChanged(nameof(PauseButtonText));
    }

    private async Task EnsureChartVisibleAsync()
    {
        if (!HasActiveSession)
        {
            return;
        }

        if (ActiveView == null || _defaultViewId <= 0)
        {
            await ReloadAsync().ConfigureAwait(false);
            return;
        }

        await LoadChartAsync().ConfigureAwait(false);
    }

    partial void OnActiveViewChanged(TrendViewViewModel? value)
    {
        OnPropertyChanged(nameof(CanUseTrendTools));
        if (value != null && HasActiveSession)
        {
            value.SetChartPaused(IsChartPaused);
            _ = LoadChartAsync();
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

            if (!HasActiveSession || _session.ConnectedConnectionId is not int connectionId || connectionId <= 0)
            {
                _connectionId = 0;
                _defaultViewId = 0;
                _viewEntity = null;
                lock (_membershipSync)
                {
                    _symbolToMonitorItemIdMap.Clear();
                    _monitorItemById.Clear();
                    _trendedMonitorItemIds.Clear();
                }
                _lastPersistedSampleUtc = DateTimeOffset.MinValue;
                _sampleCache.Clear();
                _liveBuffer.Clear();
                await ApplyChartOnMainThreadAsync(null).ConfigureAwait(false);
                return;
            }

            _connectionId = connectionId;
            await ReloadMonitorSymbolMapAsync().ConfigureAwait(false);
            if (_session.IsShuttingDown)
            {
                return;
            }

            var entity = await _trendViewRepository.GetOrCreateDefaultViewAsync(_connectionId).ConfigureAwait(false);
            _defaultViewId = entity.Id;
            _viewEntity = entity;
            await ApplyChartOnMainThreadAsync(entity).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task ApplyChartOnMainThreadAsync(TrendViewEntity? entity)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            if (entity == null)
            {
                ActiveView = null;
                NotifySessionStateChanged();
                return;
            }

            if (ActiveView?.ViewId == entity.Id)
            {
                ActiveView.ApplySettings(entity);
                ActiveView.SetChartPaused(IsChartPaused);
                NotifySessionStateChanged();
                return;
            }

            ActiveView = new TrendViewViewModel(entity);
            ActiveView.SetChartPaused(IsChartPaused);
            NotifySessionStateChanged();
        }).ConfigureAwait(false);

        if (entity != null && HasActiveSession && !_session.IsShuttingDown)
        {
            await LoadChartAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 删除监视行前<b>同步</b>摘除内存成员关系（A 竞态修复）：
    /// 否则轮询线程仍按旧成员表向 trend_samples 插入已删行的 item_id，触发 FOREIGN KEY 约束失败并整批回滚。
    /// </summary>
    public void DetachItemFromMemory(int itemId)
    {
        lock (_membershipSync)
        {
            _trendedMonitorItemIds.Remove(itemId);
            _monitorItemById.Remove(itemId);
            var staleSymbols = _symbolToMonitorItemIdMap
                .Where(kv => kv.Value == itemId)
                .Select(kv => kv.Key)
                .ToArray();
            foreach (var symbol in staleSymbols)
            {
                _symbolToMonitorItemIdMap.Remove(symbol);
            }
        }

        _liveBuffer.RemoveItem(itemId);
    }

    /// <summary>趋势成员快照：轮询线程遍历时避免与 UI 线程变更并发。</summary>
    private int[] GetTrendedItemIdSnapshot()
    {
        lock (_membershipSync)
        {
            return _trendedMonitorItemIds.ToArray();
        }
    }

    private async Task ReloadMonitorSymbolMapAsync()
    {
        lock (_membershipSync)
        {
            _symbolToMonitorItemIdMap.Clear();
            _monitorItemById.Clear();
        }

        if (_connectionId <= 0)
        {
            return;
        }

        var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(_connectionId).ConfigureAwait(false);
        var items = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(false);
        lock (_membershipSync)
        {
            foreach (var item in items)
            {
                _symbolToMonitorItemIdMap[item.Symbol] = item.Id;
                _monitorItemById[item.Id] = item;
            }
        }
    }

    private async Task ReloadMonitorMapAndChartAsync()
    {
        if (_session.IsShuttingDown || !HasActiveSession || _connectionId <= 0)
        {
            return;
        }

        await ReloadMonitorSymbolMapAsync().ConfigureAwait(false);
        if (_session.IsShuttingDown)
        {
            return;
        }

        await LoadChartAsync().ConfigureAwait(false);
    }

    private async Task LoadChartAsync()
    {
        if (_session.IsShuttingDown || !HasActiveSession || ActiveView == null || _connectionId <= 0 || _defaultViewId <= 0)
        {
            return;
        }

        var viewItems = await _trendViewRepository.GetViewItemsAsync(_defaultViewId).ConfigureAwait(false);
        if (_session.IsShuttingDown)
        {
            return;
        }

        var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(_connectionId).ConfigureAwait(false);
        var monitorItems = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(false);
        var monitorMap = monitorItems.ToDictionary(item => item.Id);
        lock (_membershipSync)
        {
            foreach (var item in monitorItems)
            {
                _monitorItemById[item.Id] = item;
            }

            _trendedMonitorItemIds.Clear();
            foreach (var viewItem in viewItems)
            {
                _trendedMonitorItemIds.Add(viewItem.ItemId);
            }
        }

        if (IsChartPaused)
        {
            await LoadHistoryChartAsync(viewItems, monitorMap).ConfigureAwait(false);
        }
        else
        {
            await LoadLiveChartAsync(viewItems, monitorMap).ConfigureAwait(false);
        }
    }

    private async Task LoadHistoryChartAsync(
        IReadOnlyList<TrendViewItemEntity>? viewItems = null,
        IReadOnlyDictionary<int, MonitorSheetItemEntity>? monitorMap = null)
    {
        await LoadHistoryChartProgressiveAsync(viewItems, monitorMap).ConfigureAwait(false);
    }

    /// <summary>
    /// 历史视图：SQLite 分页读入 + 与实时相同的 <see cref="TrendSeriesViewModel.AppendPendingSamples"/> 逐批追加。
    /// </summary>
    private async Task LoadHistoryChartProgressiveAsync(
        IReadOnlyList<TrendViewItemEntity>? viewItems = null,
        IReadOnlyDictionary<int, MonitorSheetItemEntity>? monitorMap = null)
    {
        if (_session.IsShuttingDown || !IsChartPaused || ActiveView == null || _viewEntity == null
            || _connectionId <= 0 || _defaultViewId <= 0)
        {
            return;
        }

        CancelHistoryLoad();
        _historyLoadingCts = new CancellationTokenSource();
        var ct = _historyLoadingCts.Token;

        viewItems ??= await _trendViewRepository.GetViewItemsAsync(_defaultViewId).ConfigureAwait(false);
        if (_session.IsShuttingDown || ct.IsCancellationRequested)
        {
            return;
        }

        if (monitorMap == null)
        {
            var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(_connectionId).ConfigureAwait(false);
            var monitorItems = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(false);
            monitorMap = monitorItems.ToDictionary(item => item.Id);
            lock (_membershipSync)
            {
                foreach (var item in monitorItems)
                {
                    _monitorItemById[item.Id] = item;
                }
            }
        }

        await _chartUpdateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session.IsShuttingDown || ct.IsCancellationRequested || !IsChartPaused || _viewEntity == null)
            {
                return;
            }

            var entity = _viewEntity;
            var now = DateTimeOffset.UtcNow;
            var (start, end) = TrendRangeHelper.GetVisibleRange(entity, now);

            _sampleCache.Clear();
            _sampleCache.ReplaceFull(new Dictionary<int, IReadOnlyList<TrendSampleEntity>>(), entity);

            var emptySnapshot = new Dictionary<int, IReadOnlyList<TrendSampleEntity>>();
            foreach (var itemId in GetTrendedItemIdSnapshot())
            {
                emptySnapshot[itemId] = Array.Empty<TrendSampleEntity>();
            }

            await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
            {
                if (_session.IsShuttingDown || ActiveView == null || !IsChartPaused)
                {
                    return;
                }

                ActiveView.LoadSeries(viewItems, monitorMap, emptySnapshot);
            }).ConfigureAwait(false);

            var pending = GetTrendedItemIdSnapshot()
                .Where(itemId => monitorMap.ContainsKey(itemId))
                .ToHashSet();
            var cursors = new Dictionary<int, (string TimestampUtc, int Id)>();

            while (pending.Count > 0 && !ct.IsCancellationRequested && IsChartPaused && !_session.IsShuttingDown)
            {
                if (ct.IsCancellationRequested || !IsChartPaused)
                {
                    return;
                }

                // 并行拉取各趋势变量下一页，避免 10 变量串行导致 6s 延迟（P1 优化）
                var batchIds = pending.ToArray();
                var fetchTasks = batchIds.Select(async itemId =>
                {
                    if (!monitorMap.TryGetValue(itemId, out var monitorItem))
                    {
                        return (ItemId: itemId, Page: (IReadOnlyList<TrendSampleEntity>)Array.Empty<TrendSampleEntity>(), HasItem: false);
                    }

                    cursors.TryGetValue(itemId, out var cursor);
                    var hasCursor = cursors.ContainsKey(itemId);
                    var page = await _trendSampleRepository
                        .GetSamplesBySymbolPageAsync(
                            _connectionId,
                            monitorItem.Symbol,
                            start,
                            end,
                            hasCursor ? cursor.TimestampUtc : null,
                            hasCursor ? cursor.Id : null,
                            AppConstants.HistoryChartLoadChunkSize)
                        .ConfigureAwait(false);
                    return (ItemId: itemId, Page: page, HasItem: true);
                }).ToArray();

                var batchResults = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
                if (ct.IsCancellationRequested || !IsChartPaused)
                {
                    return;
                }

                var progressed = false;
                var chunkMap = new Dictionary<int, IReadOnlyList<TrendSampleEntity>>();
                foreach (var (itemId, page, hasItem) in batchResults)
                {
                    if (!hasItem)
                    {
                        pending.Remove(itemId);
                        continue;
                    }

                    if (page.Count == 0)
                    {
                        pending.Remove(itemId);
                        continue;
                    }

                    progressed = true;
                    _sampleCache.MergeNewSamples(itemId, page);
                    var last = page[^1];
                    cursors[itemId] = (last.TimestampUtc, last.Id);

                    if (page.Count < AppConstants.HistoryChartLoadChunkSize)
                    {
                        pending.Remove(itemId);
                    }

                    chunkMap[itemId] = page;
                }

                if (chunkMap.Count > 0)
                {
                    await ApplyHistoryChunkAsync(chunkMap).ConfigureAwait(false);
                }

                if (!progressed)
                {
                    break;
                }

                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            // 用户继续播放或切换连接时取消加载。
        }
        finally
        {
            _chartUpdateLock.Release();
        }
    }

    private Task ApplyHistoryChunkAsync(IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> chunk) =>
        AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            if (_session.IsShuttingDown || ActiveView == null || !IsChartPaused)
            {
                return;
            }

            ActiveView.AppendIncrementalSamples(chunk);
        });

    private void CancelHistoryLoad()
    {
        if (_historyLoadingCts == null)
        {
            return;
        }

        try
        {
            _historyLoadingCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _historyLoadingCts.Dispose();
        _historyLoadingCts = null;
    }

    private async Task LoadLiveChartAsync(
        IReadOnlyList<TrendViewItemEntity>? viewItems = null,
        IReadOnlyDictionary<int, MonitorSheetItemEntity>? monitorMap = null)
    {
        if (_session.IsShuttingDown || ActiveView == null)
        {
            return;
        }

        await _chartUpdateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session.IsShuttingDown || ActiveView == null)
            {
                return;
            }

            TrimLiveBufferToWindow();
            var snapshot = _liveBuffer.CreateSnapshot();
            await ApplyChartFromCacheAsync(snapshot, viewItems, monitorMap).ConfigureAwait(false);
        }
        finally
        {
            _chartUpdateLock.Release();
        }
    }

    private async Task RefreshLiveChartAsync()
    {
        if (_session.IsShuttingDown || ActiveView == null || IsChartPaused)
        {
            return;
        }

        await _chartUpdateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session.IsShuttingDown || ActiveView == null || IsChartPaused)
            {
                return;
            }

            TrimLiveBufferToWindow();
            var snapshot = _liveBuffer.CreateSnapshot();
            await ApplyChartFromCacheAsync(snapshot).ConfigureAwait(false);
        }
        finally
        {
            _chartUpdateLock.Release();
        }
    }

    private void TrimLiveBufferToWindow()
    {
        if (_viewEntity == null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (start, _) = TrendRangeHelper.GetLiveVisibleRange(_viewEntity, now);
        _liveBuffer.TrimOlderThan(start);
    }

    private void AppendPollResultsToLiveBuffer(PollingCycleCompletedEventArgs args, DateTimeOffset timestamp)
    {
        foreach (var itemId in GetTrendedItemIdSnapshot())
        {
            MonitorSheetItemEntity? monitorItem;
            lock (_membershipSync)
            {
                _monitorItemById.TryGetValue(itemId, out monitorItem);
            }

            if (monitorItem == null)
            {
                continue;
            }

            if (!args.Results.TryGetValue(monitorItem.Symbol, out var result))
            {
                continue;
            }

            var sample = TrendSampleMapper.TryCreate(
                _connectionId, monitorItem.Symbol, itemId, timestamp, result);
            if (sample != null)
            {
                _liveBuffer.Append(itemId, sample);
            }
        }

        TrimLiveBufferToWindow();
    }

    private bool CanRefreshLiveChart() =>
        IsExpanded && ActiveView != null && !IsChartPaused && _trendedMonitorItemIds.Count > 0;

    /// <summary>合并刷新：轮询只标记待刷新，UI 按自身节奏 drain 缓冲，不丢中间采样点。</summary>
    private void RequestLiveChartRefresh()
    {
        if (!CanRefreshLiveChart())
        {
            return;
        }

        Volatile.Write(ref _liveRefreshPendingFlag, 1);
        _ = EnsureLiveChartRefreshAsync();
    }

    private async Task EnsureLiveChartRefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _liveChartRefreshLoopRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            while (Volatile.Read(ref _liveRefreshPendingFlag) != 0)
            {
                Volatile.Write(ref _liveRefreshPendingFlag, 0);
                await RefreshLiveChartAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _liveChartRefreshLoopRunning, 0);
            if (Volatile.Read(ref _liveRefreshPendingFlag) != 0)
            {
                _ = EnsureLiveChartRefreshAsync();
            }
        }
    }

    private int GetEffectiveSampleIntervalMs()
    {
        var sampleIntervalMs = ActiveView?.GetSampleIntervalMs()
                               ?? _viewEntity?.SampleIntervalMs
                               ?? AppConstants.DefaultSampleIntervalMs;
        var pollIntervalMs = _pollingEngine.IsRunning
            ? _pollingEngine.PollIntervalMs
            : AppConstants.DefaultPollIntervalMs;
        return TrendRangeHelper.ClampSampleIntervalMs(sampleIntervalMs, pollIntervalMs);
    }

    private async Task ApplyChartUpdateAsync(
        Func<Task> updateCache,
        IReadOnlyList<TrendViewItemEntity>? viewItems = null,
        IReadOnlyDictionary<int, MonitorSheetItemEntity>? monitorMap = null)
    {
        await _chartUpdateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            await updateCache().ConfigureAwait(false);
            if (_session.IsShuttingDown)
            {
                return;
            }

            var snapshot = _sampleCache.CreateSnapshot();
            await ApplyChartFromCacheAsync(snapshot, viewItems, monitorMap).ConfigureAwait(false);
        }
        finally
        {
            _chartUpdateLock.Release();
        }
    }

    private Task TryPersistSamplesAsync(PollingCycleCompletedEventArgs args, DateTimeOffset timestamp)
    {
        if (_session.IsShuttingDown || _trendedMonitorItemIds.Count == 0 || !args.IsSuccess)
        {
            return Task.CompletedTask;
        }

        var sampleIntervalMs = GetEffectiveSampleIntervalMs();
        var elapsedMs = (timestamp - _lastPersistedSampleUtc).TotalMilliseconds;
        if (_lastPersistedSampleUtc != DateTimeOffset.MinValue && elapsedMs < sampleIntervalMs)
        {
            return Task.CompletedTask;
        }

        return TryPersistSamplesCoreAsync(args, timestamp, sampleIntervalMs);
    }

    private async Task TryPersistSamplesCoreAsync(
        PollingCycleCompletedEventArgs args,
        DateTimeOffset timestamp,
        int sampleIntervalMs)
    {
        if (!await _persistLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            var elapsedMs = (timestamp - _lastPersistedSampleUtc).TotalMilliseconds;
            if (_lastPersistedSampleUtc != DateTimeOffset.MinValue && elapsedMs < sampleIntervalMs)
            {
                return;
            }

            var snapshotIds = GetTrendedItemIdSnapshot();
            var samples = new List<TrendSampleEntity>(snapshotIds.Length);
            foreach (var itemId in snapshotIds)
            {
                MonitorSheetItemEntity? monitorItem;
                lock (_membershipSync)
                {
                    _monitorItemById.TryGetValue(itemId, out monitorItem);
                }

                if (monitorItem == null)
                {
                    continue;
                }

                if (!args.Results.TryGetValue(monitorItem.Symbol, out var result))
                {
                    continue;
                }

                var sample = TrendSampleMapper.TryCreate(
                    _connectionId, monitorItem.Symbol, itemId, timestamp, result);
                if (sample != null)
                {
                    samples.Add(sample);
                }
            }

            if (samples.Count == 0)
            {
                return;
            }

            await _trendSampleRepository.InsertSamplesAsync(samples).ConfigureAwait(false);
            _lastPersistedSampleUtc = timestamp;
        }
        catch
        {
            // 采样写入失败不影响轮询与实时缓冲。
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private async Task<bool> UpdateSampleCacheAsync(bool fullReload)
    {
        var entity = _viewEntity;
        if (entity == null || _trendedMonitorItemIds.Count == 0 || _connectionId <= 0)
        {
            _sampleCache.Clear();
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var (start, end) = TrendRangeHelper.GetVisibleRange(entity, now);

        if (fullReload || !_sampleCache.MatchesSettings(entity))
        {
            return false;
        }

        var changed = _sampleCache.TrimOlderThan(start) > 0;
        foreach (var itemId in GetTrendedItemIdSnapshot())
        {
            MonitorSheetItemEntity? monitorItem;
            lock (_membershipSync)
            {
                _monitorItemById.TryGetValue(itemId, out monitorItem);
            }

            if (monitorItem == null)
            {
                continue;
            }

            var afterText = _sampleCache.GetMaxTimestampText(itemId);
            var newSamples = afterText == null
                ? await _trendSampleRepository
                    .GetSamplesBySymbolAsync(_connectionId, monitorItem.Symbol, start, end)
                    .ConfigureAwait(false)
                : await _trendSampleRepository
                    .GetSamplesBySymbolAfterAsync(_connectionId, monitorItem.Symbol, afterText, end)
                    .ConfigureAwait(false);
            changed |= _sampleCache.MergeNewSamples(itemId, newSamples);
        }

        return changed;
    }

    private Task ApplyChartFromCacheAsync(
        IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> snapshot,
        IReadOnlyList<TrendViewItemEntity>? viewItems = null,
        IReadOnlyDictionary<int, MonitorSheetItemEntity>? monitorMap = null)
    {
        return AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            if (_session.IsShuttingDown || ActiveView == null)
            {
                return;
            }

            if (viewItems != null && monitorMap != null)
            {
                ActiveView.LoadSeries(viewItems, monitorMap, snapshot);
                return;
            }

            if (ActiveView.IsChartPaused)
            {
                ActiveView.AppendIncrementalSamples(snapshot);
            }
            else
            {
                ActiveView.RefreshFromLiveSamples(snapshot);
            }
        });
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private async Task ToggleChartPauseAsync()
    {
        if (!CanUseTrendTools)
        {
            return;
        }

        IsChartPaused = !IsChartPaused;
        if (IsChartPaused)
        {
            await LoadHistoryChartAsync().ConfigureAwait(true);
            return;
        }

        CancelHistoryLoad();
        _liveBuffer.Clear();
        ActiveView!.ResetChartView();
        await RefreshLiveChartAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetChartViewAsync()
    {
        if (!IsChartPaused || ActiveView == null)
        {
            return;
        }

        ActiveView.ResetChartView();
        await LoadHistoryChartAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (_session.IsShuttingDown || !HasActiveSession || _defaultViewId <= 0)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = "请先连接 PLC。";
            }

            return;
        }

        var entity = await _trendViewRepository.GetViewAsync(_defaultViewId).ConfigureAwait(true)
                     ?? _viewEntity;
        if (entity == null)
        {
            _session.StatusMessage = "趋势图设置不可用。";
            return;
        }

        var pollIntervalMs = PollIntervalHelper.ParseOrDefault(
            await _appSettings.GetAsync(AppConstants.PollIntervalMsKey).ConfigureAwait(true));

        var savedEntity = await TrendSettingsPopupService
            .ShowAsync(entity, _trendViewRepository, _session.ActiveConnectionProfileName, pollIntervalMs)
            .ConfigureAwait(true);

        if (savedEntity == null || _session.IsShuttingDown)
        {
            return;
        }

        _viewEntity = savedEntity;
        _lastPersistedSampleUtc = DateTimeOffset.MinValue;
        _sampleCache.Clear();
        if (!IsChartPaused)
        {
            TrimLiveBufferToWindow();
        }

        await ApplyChartOnMainThreadAsync(_viewEntity).ConfigureAwait(true);
        await AppUiDispatcher.InvokeSafeAwaitAsync(() => _session.StatusMessage = "趋势图设置已保存。")
            .ConfigureAwait(true);
    }

    public async Task<HashSet<int>> GetTrendItemIdsAsync()
    {
        if (!HasActiveSession || _session.ConnectedConnectionId is not int connectionId || connectionId <= 0)
        {
            return [];
        }

        if (_connectionId <= 0)
        {
            _connectionId = connectionId;
        }

        if (_defaultViewId <= 0)
        {
            var view = await _trendViewRepository.GetOrCreateDefaultViewAsync(connectionId).ConfigureAwait(false);
            _defaultViewId = view.Id;
        }

        var items = await _trendViewRepository.GetViewItemsAsync(_defaultViewId).ConfigureAwait(false);
        return items.Select(item => item.ItemId).ToHashSet();
    }

    public Task ToggleItemTrendAsync(MonitorSheetItemViewModel row) =>
        row.IsInTrend
            ? RemoveItemFromTrendAsync(row.ItemId, row.Symbol)
            : AddItemToTrendAsync(row.ItemId, row.Symbol);

    public async Task AddItemToTrendAsync(int itemId, string symbol)
    {
        if (_session.IsShuttingDown || !HasActiveSession)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = "请先连接 PLC。";
            }

            return;
        }

        await _membershipGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await EnsureDefaultViewAsync().ConfigureAwait(true);

            if (_trendedMonitorItemIds.Count >= AppConstants.MaxTrendItems)
            {
                _session.StatusMessage = $"趋势已达上限 {AppConstants.MaxTrendItems} 项，请先移除部分变量。";
                return;
            }

            if (await _trendViewRepository.ViewContainsItemAsync(_defaultViewId, itemId).ConfigureAwait(true))
            {
                IsExpanded = true;
                await LoadChartAsync().ConfigureAwait(true);
                _session.NotifyTrendMembershipChanged();
                _session.StatusMessage = $"「{symbol}」已在趋势图中。";
                return;
            }

            // 二次校验 DB 侧数量，防止并发绕过内存检查
            var viewItems = await _trendViewRepository.GetViewItemsAsync(_defaultViewId).ConfigureAwait(true);
            if (viewItems.Count >= AppConstants.MaxTrendItems)
            {
                lock (_membershipSync)
                {
                    _trendedMonitorItemIds.Clear();
                    foreach (var vi in viewItems) _trendedMonitorItemIds.Add(vi.ItemId);
                }
                _session.StatusMessage = $"趋势已达上限 {AppConstants.MaxTrendItems} 项，请先移除部分变量。";
                return;
            }

            await _trendViewRepository.AddItemToViewAsync(_defaultViewId, itemId).ConfigureAwait(true);
            await LoadChartAsync().ConfigureAwait(true);
            IsExpanded = true;
            _session.NotifyTrendMembershipChanged();
            _session.StatusMessage = $"已加入趋势图：{symbol}";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"加入趋势图失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            _membershipGate.Release();
        }
    }

    public async Task RemoveItemFromTrendAsync(int itemId, string symbol)
    {
        if (_session.IsShuttingDown || !HasActiveSession)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = "请先连接 PLC。";
            }

            return;
        }

        await _membershipGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await EnsureDefaultViewAsync().ConfigureAwait(true);

            if (!await _trendViewRepository.ViewContainsItemAsync(_defaultViewId, itemId).ConfigureAwait(true))
            {
                _session.StatusMessage = $"「{symbol}」不在趋势图中。";
                _session.NotifyTrendMembershipChanged();
                return;
            }

            await _trendViewRepository.RemoveItemFromViewAsync(_defaultViewId, itemId).ConfigureAwait(true);
            _liveBuffer.RemoveItem(itemId);
            await LoadChartAsync().ConfigureAwait(true);
            _session.NotifyTrendMembershipChanged();
            _session.StatusMessage = $"已从趋势图移除：{symbol}";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"从趋势图移除失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            _membershipGate.Release();
        }
    }

    private async Task EnsureDefaultViewAsync()
    {
        if (_connectionId <= 0 && _session.ConnectedConnectionId is int connectedId && connectedId > 0)
        {
            _connectionId = connectedId;
        }

        if (_defaultViewId > 0)
        {
            return;
        }

        var view = await _trendViewRepository.GetOrCreateDefaultViewAsync(_connectionId).ConfigureAwait(true);
        _defaultViewId = view.Id;
        _viewEntity = view;
    }

    private async Task LoadExpandedStateAsync()
    {
        var text = await _appSettings.GetAsync(AppConstants.TrendPanelExpandedKey).ConfigureAwait(false);
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (bool.TryParse(text, out var expanded) && expanded)
        {
            await AppUiDispatcher.InvokeSafeAwaitAsync(() => IsExpanded = true).ConfigureAwait(false);
        }
    }

    private Task SaveExpandedStateAsync(bool expanded) =>
        _appSettings.SetAsync(AppConstants.TrendPanelExpandedKey, expanded ? "true" : "false");

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(HasNoActiveSession));
        OnPropertyChanged(nameof(CanUseTrendTools));
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(EmptyHint));
    }
}
