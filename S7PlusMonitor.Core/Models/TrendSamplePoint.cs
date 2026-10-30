namespace S7PlusMonitor.Core.Models;

/// <summary>趋势采样点（时间戳 + 数值/布尔 + Quality）。</summary>

public sealed class TrendSamplePoint
{
    public required DateTimeOffset Timestamp { get; init; }

    public required double Value { get; init; }

    public required VariableReadQuality Quality { get; init; }
}
