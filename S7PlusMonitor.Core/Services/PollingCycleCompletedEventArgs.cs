using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Services;

public sealed class PollingCycleCompletedEventArgs : EventArgs
{
    /// <summary>本轮 PLC 读耗时（微秒）。</summary>
    public required long DurationMicroseconds { get; init; }

    public required int ItemCount { get; init; }

    public required int SuccessCount { get; init; }

    public required bool IsSuccess { get; init; }

    public required IReadOnlyDictionary<string, VariableReadResult> Results { get; init; }
}
