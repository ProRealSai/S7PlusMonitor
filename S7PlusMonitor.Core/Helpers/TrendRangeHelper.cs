using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

public static class TrendRangeHelper
{
    public static bool IsValidSampleIntervalMs(int intervalMs) =>
        intervalMs is >= AppConstants.MinSampleIntervalMs and <= AppConstants.MaxSampleIntervalMs;

    public static int GetMinSampleIntervalMs(int pollIntervalMs)
    {
        pollIntervalMs = PollIntervalHelper.Clamp(pollIntervalMs);
        if (pollIntervalMs <= 0)
        {
            return AppConstants.MinSampleIntervalMs;
        }

        return Math.Max(AppConstants.MinSampleIntervalMs, pollIntervalMs);
    }

    public static bool IsValidSampleIntervalMs(int sampleIntervalMs, int pollIntervalMs)
    {
        if (!IsValidSampleIntervalMs(sampleIntervalMs))
        {
            return false;
        }

        pollIntervalMs = PollIntervalHelper.Clamp(pollIntervalMs);
        return pollIntervalMs <= 0 || sampleIntervalMs >= pollIntervalMs;
    }

    public static int ClampSampleIntervalMs(int intervalMs, int pollIntervalMs) =>
        Math.Clamp(
            Math.Max(intervalMs, GetMinSampleIntervalMs(pollIntervalMs)),
            AppConstants.MinSampleIntervalMs,
            AppConstants.MaxSampleIntervalMs);

    /// <summary>将监视周期映射为合法的历史采样间隔（连续轮询时为最低 10 ms）。</summary>
    public static int SampleIntervalFromPollInterval(int pollIntervalMs) =>
        ClampSampleIntervalMs(PollIntervalHelper.Clamp(pollIntervalMs), PollIntervalHelper.Clamp(pollIntervalMs));

    public static string FormatSampleIntervalRule(int pollIntervalMs) =>
        pollIntervalMs > 0
            ? $"须不小于监视周期 {pollIntervalMs} ms（当前连接 Tab 设置，可等于）"
            : $"连续轮询时最低 {AppConstants.MinSampleIntervalMs} ms";

    public static int ClampSampleIntervalMs(int intervalMs) =>
        Math.Clamp(intervalMs, AppConstants.MinSampleIntervalMs, AppConstants.MaxSampleIntervalMs);

    public static string SampleIntervalRangeHint =>
        $"{AppConstants.MinSampleIntervalMs}–{AppConstants.MaxSampleIntervalMs} ms";

    public const int MinHistoryRollingWindowSeconds = 10;
    public const int MaxHistoryRollingWindowDays = 360;
    public const int MaxHistoryRollingWindowSeconds = MaxHistoryRollingWindowDays * 86_400;

    /// <summary>实时视图滚动窗口：1 秒–60 分钟。</summary>
    public const int MinLiveRollingWindowSeconds = 1;
    public const int MaxLiveRollingWindowSeconds = 3_600;
    public const int MaxLiveRollingWindowMinutes = MaxLiveRollingWindowSeconds / 60;

    public static bool IsValidHistoryRollingWindow(int seconds) =>
        seconds is >= MinHistoryRollingWindowSeconds and <= MaxHistoryRollingWindowSeconds;

    public static int ClampHistoryRollingWindow(int seconds) =>
        Math.Clamp(seconds, MinHistoryRollingWindowSeconds, MaxHistoryRollingWindowSeconds);

    public static bool IsValidLiveRollingWindow(int seconds) =>
        seconds is >= MinLiveRollingWindowSeconds and <= MaxLiveRollingWindowSeconds;

    public static int ClampLiveRollingWindow(int seconds) =>
        Math.Clamp(seconds, MinLiveRollingWindowSeconds, MaxLiveRollingWindowSeconds);

    public static string LiveRollingWindowRangeHint => "1 秒至 60 分钟";

    public static (int Value, TrendDurationUnit Unit) DecomposeLiveRollingSeconds(int seconds)
    {
        seconds = ClampLiveRollingWindow(seconds);
        if (seconds >= 60 && seconds % 60 == 0)
        {
            return (seconds / 60, TrendDurationUnit.Minute);
        }

        return (seconds, TrendDurationUnit.Second);
    }

    public static string FormatLiveRollingWindow(int seconds)
    {
        var (value, unit) = DecomposeLiveRollingSeconds(seconds);
        return $"{value} {GetDurationUnitLabel(unit)}";
    }

    public static int ToSeconds(int value, TrendDurationUnit unit)
    {
        if (value <= 0)
        {
            return 0;
        }

        var multiplier = unit switch
        {
            TrendDurationUnit.Second => 1,
            TrendDurationUnit.Minute => 60,
            TrendDurationUnit.Hour => 3_600,
            TrendDurationUnit.Day => 86_400,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
        };

        var seconds = (long)value * multiplier;
        return seconds > int.MaxValue ? int.MaxValue : (int)seconds;
    }

    public static (int Value, TrendDurationUnit Unit) DecomposeHistoryRollingSeconds(int seconds)
    {
        seconds = ClampHistoryRollingWindow(seconds);

        if (seconds >= 86_400 && seconds % 86_400 == 0)
        {
            return (seconds / 86_400, TrendDurationUnit.Day);
        }

        if (seconds >= 3_600 && seconds % 3_600 == 0)
        {
            return (seconds / 3_600, TrendDurationUnit.Hour);
        }

        if (seconds >= 60 && seconds % 60 == 0)
        {
            return (seconds / 60, TrendDurationUnit.Minute);
        }

        return (seconds, TrendDurationUnit.Second);
    }

    public static string GetDurationUnitLabel(TrendDurationUnit unit) =>
        unit switch
        {
            TrendDurationUnit.Day => "天",
            TrendDurationUnit.Hour => "小时",
            TrendDurationUnit.Minute => "分钟",
            TrendDurationUnit.Second => "秒",
            _ => unit.ToString()
        };

    public static string FormatHistoryRollingWindow(int seconds)
    {
        var (value, unit) = DecomposeHistoryRollingSeconds(seconds);
        return $"{value} {GetDurationUnitLabel(unit)}";
    }

    public static string HistoryRollingWindowRangeHint =>
        $"{MinHistoryRollingWindowSeconds} 秒至 {MaxHistoryRollingWindowDays} 天";

    public static (DateTimeOffset Start, DateTimeOffset End) GetVisibleRange(
        TrendViewEntity settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.XRangeMode == TrendXRangeMode.Rolling)
        {
            var window = ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds);
            return (now.AddSeconds(-window), now);
        }

        var start = ParseUtc(settings.XRangeStartUtc)
                    ?? now.AddSeconds(-ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds));
        var end = settings.XRangeEndFollowsNow
            ? now
            : ParseUtc(settings.XRangeEndUtc) ?? now;

        if (end < start)
        {
            end = start;
        }

        return (start, end);
    }

    /// <summary>实时视图可见范围：仅滚动时长，忽略绝对时间轴设置。</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) GetLiveVisibleRange(
        TrendViewEntity settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var window = ClampLiveRollingWindow(settings.LiveRollingWindowSeconds);
        return (now.AddSeconds(-window), now);
    }

    public static (bool AutoScale, double? Min, double? Max) GetYRange(TrendViewEntity settings, bool isHistoryView) =>
        isHistoryView
            ? (settings.HistoryYAutoScale, settings.HistoryYMin, settings.HistoryYMax)
            : (settings.LiveYAutoScale, settings.LiveYMin, settings.LiveYMax);

    public static int GetRetentionSeconds(TrendViewEntity settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.XRangeMode == TrendXRangeMode.Rolling)
        {
            return ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds);
        }

        var start = ParseUtc(settings.XRangeStartUtc);
        var end = settings.XRangeEndFollowsNow
            ? DateTimeOffset.UtcNow
            : ParseUtc(settings.XRangeEndUtc) ?? DateTimeOffset.UtcNow;

        if (start == null)
        {
            return ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds);
        }

        var span = Math.Max(
            ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds),
            (int)(end - start.Value).TotalSeconds);
        return Math.Clamp(span, MinHistoryRollingWindowSeconds, MaxHistoryRollingWindowSeconds);
    }

    public static bool IsValidManualYRange(double? yMin, double? yMax) =>
        yMin is double min && yMax is double max && min < max;

    public static bool IsValidAbsoluteRange(
        DateTimeOffset? start,
        DateTimeOffset? end,
        bool endFollowsNow)
    {
        if (start == null)
        {
            return false;
        }

        if (endFollowsNow)
        {
            return true;
        }

        return end is DateTimeOffset fixedEnd && fixedEnd >= start;
    }

    public static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    public static DateTimeOffset? ParseUtc(string? text) =>
        DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUniversalTime() : null;

    /// <summary>历史视图 X 轴时间标签（恒显毫秒，适配 ≥10ms 快速采样）。</summary>
    public const string ChartAxisDateTimeFormat = "HH:mm:ss.fff";

    /// <summary>统一 X 轴标签：恒显毫秒。</summary>
    public static string FormatChartAxisDateTime(DateTime dateTime) =>
        dateTime.ToString(ChartAxisDateTimeFormat);

    public static string FormatChartAxisDateTime(DateTimeOffset dateTime) =>
        FormatChartAxisDateTime(dateTime.LocalDateTime);

    /// <summary>实时视图轴刻度步长（秒，可小于 1），与 1 秒–60 分钟窗口匹配。</summary>
    public static double ComputeLiveAxisLabelStepSeconds(int visibleSpanSeconds)
    {
        visibleSpanSeconds = ClampLiveRollingWindow(visibleSpanSeconds);
        if (visibleSpanSeconds <= 3)
        {
            return 0.2;
        }

        if (visibleSpanSeconds <= 10)
        {
            return 0.5;
        }

        if (visibleSpanSeconds <= 60)
        {
            return 1;
        }

        if (visibleSpanSeconds <= 900)
        {
            return 60;
        }

        if (visibleSpanSeconds <= 1_800)
        {
            return 120;
        }

        return 300;
    }

    public static double ComputeAxisLabelStepSeconds(int visibleSpanSeconds)
    {
        if (visibleSpanSeconds <= 120)
        {
            return 10;
        }

        if (visibleSpanSeconds <= 600)
        {
            return 30;
        }

        if (visibleSpanSeconds <= 3600)
        {
            return 60;
        }

        if (visibleSpanSeconds <= 86_400)
        {
            return 3_600;
        }

        if (visibleSpanSeconds <= 604_800)
        {
            return 86_400;
        }

        if (visibleSpanSeconds <= 2_592_000)
        {
            return 86_400 * 7;
        }

        return 86_400 * 30;
    }
}
