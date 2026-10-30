namespace S7PlusMonitor.Core.Helpers;

public static class PollingStatusFormatter
{
    private const long MicrosecondsPerMillisecond = 1000;

    /// <summary>≥1ms 显示 ms；&lt;1ms 显示 μs。</summary>
    public static string FormatDuration(long microseconds)
    {
        if (microseconds <= 0)
        {
            return "<1μs";
        }

        if (microseconds < MicrosecondsPerMillisecond)
        {
            return $"{microseconds}μs";
        }

        var milliseconds = microseconds / (double)MicrosecondsPerMillisecond;
        if (Math.Abs(milliseconds - Math.Round(milliseconds)) < 0.05)
        {
            return $"{(long)Math.Round(milliseconds)}ms";
        }

        return $"{milliseconds:0.#}ms";
    }

    public static string FormatPollInterval(int pollIntervalMs) =>
        pollIntervalMs == 0 ? "连续轮询" : $"周期 {pollIntervalMs}ms";

    public static string FormatActiveCycleStatus(
        long currentMicroseconds,
        long? minMicroseconds,
        long? maxMicroseconds,
        int pollIntervalMs,
        bool succeeded)
    {
        var intervalText = FormatPollInterval(pollIntervalMs);
        if (!minMicroseconds.HasValue || !maxMicroseconds.HasValue)
        {
            return succeeded
                ? $"当前 {FormatDuration(currentMicroseconds)} · {intervalText}"
                : $"读取失败 · 当前 {FormatDuration(currentMicroseconds)} · {intervalText}";
        }

        var timingText =
            $"当前 {FormatDuration(currentMicroseconds)} · 最短 {FormatDuration(minMicroseconds.Value)} · 最长 {FormatDuration(maxMicroseconds.Value)}";

        return succeeded
            ? $"{timingText} · {intervalText}"
            : $"读取失败 · {timingText} · {intervalText}";
    }

    /// <summary>
    /// 托盘 Tooltip 用：仅保留「当前耗时 + 周期/连续轮询」，省略最短/最长等长字段。
    /// </summary>
    public static string CompactForTrayTooltip(string pollingStatusText)
    {
        if (string.IsNullOrWhiteSpace(pollingStatusText))
        {
            return "—";
        }

        if (pollingStatusText.Contains("监视未启动", StringComparison.Ordinal))
        {
            return "未启动";
        }

        if (pollingStatusText.Contains("监视读取已暂停", StringComparison.Ordinal))
        {
            return "读取已暂停";
        }

        if (pollingStatusText.StartsWith("监视待机 · ", StringComparison.Ordinal))
        {
            return pollingStatusText["监视待机 · ".Length..];
        }

        var interval = ExtractInterval(pollingStatusText);
        var current = ExtractCurrentDuration(pollingStatusText);
        var failed = pollingStatusText.StartsWith("读取失败", StringComparison.Ordinal);

        if (current != null)
        {
            return failed
                ? $"读取失败 · 当前 {current} · {interval}"
                : $"当前 {current} · {interval}";
        }

        return interval;
    }

    private static string? ExtractCurrentDuration(string text)
    {
        const string marker = "当前 ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = text.IndexOf('·', start);
        if (end < 0)
        {
            return text[start..].Trim();
        }

        return text[start..end].Trim();
    }

    private static string ExtractInterval(string text)
    {
        if (text.Contains("连续轮询", StringComparison.Ordinal))
        {
            return "连续轮询";
        }

        const string marker = "周期 ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return "—";
        }

        start += marker.Length;
        var end = text.IndexOf('·', start);
        return end < 0
            ? $"周期 {text[start..].Trim()}"
            : $"周期 {text[start..end].Trim()}";
    }
}
