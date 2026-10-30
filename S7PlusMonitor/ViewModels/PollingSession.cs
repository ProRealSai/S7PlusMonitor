using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.Services;

namespace S7PlusMonitor.ViewModels;

/// <summary>
/// 轮询会话（A1 拆分，源 <c>AppSessionViewModel</c> 的轮询职责）：
/// 独占持有 <see cref="PollingMonitorEngine"/> 的启停/暂停调用与状态栏文本计算；
/// <see cref="AppSessionViewModel"/> 仅做门面转发与 PropertyChanged 转发。
/// </summary>
internal sealed class PollingSession
{
    private readonly PollingMonitorEngine _engine;
    private readonly Func<bool> _isConnectedProvider;
    private DateTimeOffset _lastStatusBarUpdateUtc = DateTimeOffset.MinValue;
    private bool _detached;

    public PollingSession(PollingMonitorEngine engine, Func<bool> isConnectedProvider)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _isConnectedProvider = isConnectedProvider;
        _engine.CycleCompleted += OnEngineCycleCompleted;
    }

    /// <summary>状态文本变化后的回调（UI 线程）；AppSession 借此转发 OnPropertyChanged。</summary>
    public event Action? StatusUpdated;

    public bool IsRunning => _engine.IsRunning;

    public bool IsSuspended => _engine.IsSuspended;

    public int PollIntervalMs => _engine.PollIntervalMs;

    public int LastPolledItemCount => _engine.LastPolledItemCount;

    public string PollingStatusText { get; private set; } = string.Empty;

    public bool HasPollingStatus => !string.IsNullOrEmpty(PollingStatusText);

    public DateTime? LastPollingUpdateLocalTime { get; private set; }

    /// <summary>监视行增删后使引擎符号缓存失效。</summary>
    public void InvalidateSymbolCache() => _engine.InvalidateSymbolCache();

    /// <summary>暂停/恢复监视读取（浏览等独占 PLC 操作期间）。</summary>
    public void SetSuspended(bool suspended) => _engine.SetMonitoringSuspended(suspended);

    public Task StartAsync(int connectionId) => _engine.StartAsync(connectionId);

    public Task StopAsync() => _engine.StopAsync();

    /// <summary>请求刷新状态栏（自动调度到 UI 线程，已在主线程则内联执行）。</summary>
    public void NotifyStatusChanged(bool force = false) =>
        AppUiDispatcher.InvokeSafe(() => UpdateStatusCore(null, force));

    /// <summary>退出阶段解除引擎事件订阅，避免销毁阶段再调度。</summary>
    public void DetachFromEngine()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _engine.CycleCompleted -= OnEngineCycleCompleted;
    }

    private void OnEngineCycleCompleted(object? sender, PollingCycleCompletedEventArgs args)
    {
        if (_detached)
        {
            return;
        }

        AppUiDispatcher.InvokeSafeAsync(() => UpdateStatusCore(args, force: false));
    }

    private void UpdateStatusCore(PollingCycleCompletedEventArgs? cycle, bool force)
    {
        if (!force && cycle != null)
        {
            var elapsedMs = (DateTimeOffset.UtcNow - _lastStatusBarUpdateUtc).TotalMilliseconds;
            if (elapsedMs < AppConstants.PollingStatusUiIntervalMs)
            {
                return;
            }
        }

        _lastStatusBarUpdateUtc = DateTimeOffset.UtcNow;

        if (!_isConnectedProvider())
        {
            PollingStatusText = string.Empty;
        }
        else if (_engine.IsSuspended)
        {
            PollingStatusText = "监视读取已暂停";
        }
        else if (!_engine.IsRunning)
        {
            PollingStatusText = "监视未启动 · 点击 ▶ 开始监视";
        }
        else if (_engine.LastPolledItemCount == 0)
        {
            PollingStatusText = $"监视待机 · {PollingStatusFormatter.FormatPollInterval(_engine.PollIntervalMs)}";
        }
        else
        {
            var currentMicroseconds = cycle?.DurationMicroseconds ?? _engine.LastCycleDurationMicroseconds;
            if (currentMicroseconds <= 0 && _engine.LastCycleDurationMicroseconds > 0)
            {
                currentMicroseconds = _engine.LastCycleDurationMicroseconds;
            }

            var succeeded = cycle?.IsSuccess ?? _engine.LastCycleSucceeded;
            PollingStatusText = PollingStatusFormatter.FormatActiveCycleStatus(
                currentMicroseconds,
                _engine.MinCycleDurationMicroseconds,
                _engine.MaxCycleDurationMicroseconds,
                _engine.PollIntervalMs,
                succeeded);
        }

        StatusUpdated?.Invoke();

        if (cycle != null && _engine.IsRunning && !_engine.IsSuspended)
        {
            LastPollingUpdateLocalTime = DateTime.Now;
        }
    }
}
