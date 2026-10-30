using System.Diagnostics;
using System.Runtime;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Services;

/// <summary>PLC 周期读监视表符号；循环时长仅含读 PLC + 符号解析，不含订阅方处理。</summary>
public sealed class PollingMonitorEngine
{
    private const int DefaultPollIntervalMs = AppConstants.DefaultPollIntervalMs;

    private readonly IPlcService _plcService;
    private readonly IMonitorSheetRepository _monitorSheetRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _connectionId;
    private volatile bool _suspended;
    private volatile bool _cycleEventsEnabled = true;
    /// <summary>去重后的监视符号集缓存；由 <see cref="InvalidateSymbolCache"/> 置脏后在下一轮重载，避免每轮 DB 查询。</summary>
    private string[] _distinctMonitorSymbols = [];
    private volatile bool _needsSymbolReload = true;
    /// <summary>用户配置的原始监视周期（0=连续轮询）；实际生效值= max(配置, 10ms)。</summary>
    private int _configuredPollIntervalMs = DefaultPollIntervalMs;

    public PollingMonitorEngine(
        IPlcService plcService,
        IMonitorSheetRepository monitorSheetRepository,
        IAppSettingsRepository appSettings)
    {
        _plcService = plcService;
        _monitorSheetRepository = monitorSheetRepository;
        _appSettings = appSettings;
    }

    public event EventHandler<PollingCycleCompletedEventArgs>? CycleCompleted;

    public bool IsRunning { get; private set; }

    public bool IsSuspended => _suspended;

    public int PollIntervalMs { get; private set; } = DefaultPollIntervalMs;

    public long LastCycleDurationMicroseconds { get; private set; }

    public long? MinCycleDurationMicroseconds { get; private set; }

    public long? MaxCycleDurationMicroseconds { get; private set; }

    public int LastPolledItemCount { get; private set; }

    public bool LastCycleSucceeded { get; private set; } = true;

    /// <summary>标记监视符号缓存失效，下一轮将从 DB 重新加载 Distinct 符号集。</summary>
    public void InvalidateSymbolCache() => _needsSymbolReload = true;

    /// <summary>设置监视是否暂停；暂停时轮询循环按间隔休眠，不执行 PLC 读取。</summary>
    public void SetMonitoringSuspended(bool suspended) => _suspended = suspended;

    public async Task StartAsync(int connectionId, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && _connectionId == connectionId && _loopCts is { IsCancellationRequested: false })
            {
                _configuredPollIntervalMs = await LoadConfiguredPollIntervalMsAsync().ConfigureAwait(false);
                PollIntervalMs = PollIntervalHelper.ResolveEffectiveIntervalMs(_configuredPollIntervalMs);
                _suspended = false;
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            _connectionId = connectionId;
            _needsSymbolReload = true;
            _configuredPollIntervalMs = await LoadConfiguredPollIntervalMsAsync().ConfigureAwait(false);
            PollIntervalMs = PollIntervalHelper.ResolveEffectiveIntervalMs(_configuredPollIntervalMs);
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            _cycleEventsEnabled = true;
            _suspended = false;
            _loopTask = RunLoopAsync(_loopCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        _cycleEventsEnabled = false;
        _suspended = true;

        if (_loopCts == null)
        {
            IsRunning = false;
            _connectionId = 0;
            _distinctMonitorSymbols = [];
            _needsSymbolReload = true;
            LastCycleDurationMicroseconds = 0;
            MinCycleDurationMicroseconds = null;
            MaxCycleDurationMicroseconds = null;
            LastPolledItemCount = 0;
            LastCycleSucceeded = true;
            return;
        }

        _loopCts.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when stopping loop
            }
            catch (Exception)
            {
                // 驱动 TLS 读线程取消可能以其他异常冒出，停止阶段忽略
            }

            _loopTask = null;
        }

        _loopCts.Dispose();
        _loopCts = null;
        var shouldCompact = LastPolledItemCount > 10_000 || GC.GetTotalMemory(false) > 500_000_000;
        IsRunning = false;
        _connectionId = 0;
        _distinctMonitorSymbols = [];
        _needsSymbolReload = true;
        LastCycleDurationMicroseconds = 0;
        MinCycleDurationMicroseconds = null;
        MaxCycleDurationMicroseconds = null;
        LastPolledItemCount = 0;
        LastCycleSucceeded = true;
        if (shouldCompact)
        {
            // 50k 场景单轮 20MB LOH 碎片，仅大表或高内存时压缩，避免频繁 STW
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Optimized, true, true);
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_plcService.IsConnected)
                {
                    break;
                }

                if (_suspended)
                {
                    var suspendDelay = PollIntervalMs;
                    await Task.Delay(suspendDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var startedAt = Environment.TickCount64;
                var cycle = await ExecuteCycleAsync(cancellationToken).ConfigureAwait(false);
                RecordCycleDuration(cycle);
                LastPolledItemCount = cycle.ItemCount;
                LastCycleSucceeded = cycle.IsSuccess;
                // 实际生效间隔：单一 10ms 下限（配置 0=连续轮询按 10ms 执行）
                PollIntervalMs = PollIntervalHelper.ResolveEffectiveIntervalMs(_configuredPollIntervalMs);
                // 事件在池线程派发，避免趋势/UI 订阅方拖长通讯循环。
                if (_cycleEventsEnabled)
                {
                    var capturedCycle = cycle;
                    _ = Task.Run(() => CycleCompleted?.Invoke(this, capturedCycle), cancellationToken);
                }

                var elapsed = (int)(Environment.TickCount64 - startedAt);
                var delay = Math.Max(0, PollIntervalMs - elapsed);
                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stop requested
        }
    }

    private async Task<PollingCycleCompletedEventArgs> ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (_connectionId <= 0)
        {
            return EmptyCycle(stopwatch, isSuccess: true);
        }

        try
        {
            var symbols = await ResolveSymbolsAsync(cancellationToken).ConfigureAwait(false);
            if (symbols.Length == 0)
            {
                return EmptyCycle(stopwatch, isSuccess: true);
            }

            var readResults = await _plcService.ReadVariablesAsync(symbols, cancellationToken).ConfigureAwait(false);
            // 复用字典：单次遍历完成 ToDictionary + SuccessCount，避免 2 次 LINQ 分配（50k 时 20MB LOH）
            var resultMap = new Dictionary<string, VariableReadResult>(readResults.Count, StringComparer.OrdinalIgnoreCase);
            var successCount = 0;
            foreach (var item in readResults)
            {
                resultMap[item.Symbol] = item;
                if (item.Quality == VariableReadQuality.Good)
                    successCount++;
            }
            stopwatch.Stop();

            return new PollingCycleCompletedEventArgs
            {
                DurationMicroseconds = ToMicroseconds(stopwatch),
                ItemCount = symbols.Length,
                SuccessCount = successCount,
                IsSuccess = successCount > 0 || symbols.Length == 0,
                Results = resultMap
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return EmptyCycle(stopwatch, isSuccess: false);
        }
        catch (InvalidOperationException)
        {
            stopwatch.Stop();
            return EmptyCycle(stopwatch, isSuccess: false);
        }
        catch
        {
            stopwatch.Stop();
            return new PollingCycleCompletedEventArgs
            {
                DurationMicroseconds = ToMicroseconds(stopwatch),
                ItemCount = LastPolledItemCount,
                SuccessCount = 0,
                IsSuccess = false,
                Results = new Dictionary<string, VariableReadResult>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    private async Task<string[]> ResolveSymbolsAsync(CancellationToken cancellationToken)
    {
        if (!_needsSymbolReload)
        {
            return _distinctMonitorSymbols;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(_connectionId).ConfigureAwait(false);
        var items = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(false);
        var symbols = items
            .Select(item => item.Symbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _distinctMonitorSymbols = symbols;
        _needsSymbolReload = false;
        return symbols;
    }

    private static PollingCycleCompletedEventArgs EmptyCycle(Stopwatch stopwatch, bool isSuccess)
    {
        stopwatch.Stop();
        return new PollingCycleCompletedEventArgs
        {
            DurationMicroseconds = ToMicroseconds(stopwatch),
            ItemCount = 0,
            SuccessCount = 0,
            IsSuccess = isSuccess,
            Results = new Dictionary<string, VariableReadResult>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RecordCycleDuration(PollingCycleCompletedEventArgs cycle)
    {
        if (cycle.ItemCount <= 0)
        {
            return;
        }

        var duration = Math.Max(0, cycle.DurationMicroseconds);
        LastCycleDurationMicroseconds = duration;
        MinCycleDurationMicroseconds = MinCycleDurationMicroseconds.HasValue
            ? Math.Min(MinCycleDurationMicroseconds.Value, duration)
            : duration;
        MaxCycleDurationMicroseconds = MaxCycleDurationMicroseconds.HasValue
            ? Math.Max(MaxCycleDurationMicroseconds.Value, duration)
            : duration;
    }

    private static long ToMicroseconds(Stopwatch stopwatch) =>
        (long)Math.Max(0, stopwatch.Elapsed.TotalMicroseconds);

    /// <summary>读取用户配置的原始监视周期（0=连续轮询，不在此处钳制；生效值= max(配置, 10ms)）。</summary>
    private async Task<int> LoadConfiguredPollIntervalMsAsync()
    {
        var text = await _appSettings.GetAsync(AppConstants.PollIntervalMsKey).ConfigureAwait(false);
        return PollIntervalHelper.ParseOrDefault(text);
    }
}
