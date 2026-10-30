using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;
using SkiaSharp;

namespace S7PlusMonitor.ViewModels;

public sealed class TrendSeriesViewModel
{
    private static readonly SKColor[] SeriesColors =
    [
        SKColor.Parse("#2563EB"),
        SKColor.Parse("#DC2626"),
        SKColor.Parse("#16A34A"),
        SKColor.Parse("#9333EA"),
        SKColor.Parse("#EA580C"),
        SKColor.Parse("#0891B2"),
        SKColor.Parse("#CA8A04"),
        SKColor.Parse("#BE185D")
    ];

    public TrendSeriesViewModel(int itemId, string symbol, int colorIndex, string? displayColorHex = null)
    {
        ItemId = itemId;
        Symbol = symbol;

        var strokeColor = TryParseColor(displayColorHex) ?? SeriesColors[colorIndex % SeriesColors.Length];
        Series = new LineSeries<DateTimePoint>
        {
            Name = symbol,
            Values = Points,
            Stroke = new SolidColorPaint(strokeColor) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = 0,
            AnimationsSpeed = TimeSpan.Zero
        };
    }

    public int ItemId { get; }

    public string Symbol { get; }

    public ObservableCollection<DateTimePoint> Points { get; } = [];

    public LineSeries<DateTimePoint> Series { get; }

    public void LoadFromSamples(IReadOnlyList<TrendSampleEntity> samples)
    {
        UpdateFromSamples(samples, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
    }

    /// <summary>实时播放：仅追加/更新最新点，避免全量替换触发图表动画。</summary>
    public void AppendOrUpdateLatest(TrendSampleEntity sample, DateTimeOffset windowStart)
    {
        var point = TrendSampleMapper.TryToPoint(sample);
        if (point == null)
        {
            return;
        }

        AppendOrUpdateLatest(point, windowStart);
    }

    public void AppendOrUpdateLatest(TrendSamplePoint point, DateTimeOffset windowStart)
    {
        var localTime = point.Timestamp.LocalDateTime;
        if (Points.Count > 0 && Points[^1].DateTime == localTime)
        {
            Points[^1] = new DateTimePoint(localTime, point.Value);
        }
        else
        {
            Points.Add(new DateTimePoint(localTime, point.Value));
        }

        TrimOlderThan(windowStart);
    }

    /// <summary>将缓冲中尚未绘制的采样追加到序列，保留方波等快速跳变。</summary>
    public void AppendPendingSamples(IReadOnlyList<TrendSampleEntity> samples, DateTimeOffset windowStart)
    {
        if (samples.Count == 0)
        {
            TrimOlderThan(windowStart);
            return;
        }

        DateTime? lastChartTime = Points.Count > 0 ? Points[^1].DateTime : null;
        foreach (var sample in samples)
        {
            var point = TrendSampleMapper.TryToPoint(sample);
            if (point == null)
            {
                continue;
            }

            var localTime = point.Timestamp.LocalDateTime;
            if (lastChartTime.HasValue && localTime <= lastChartTime.Value)
            {
                continue;
            }

            Points.Add(new DateTimePoint(localTime, point.Value));
            lastChartTime = localTime;
        }

        TrimOlderThan(windowStart);
    }

    public void TrimOlderThan(DateTimeOffset cutoff)
    {
        var localCutoff = cutoff.LocalDateTime;
        while (Points.Count > 0 && Points[0].DateTime < localCutoff)
        {
            Points.RemoveAt(0);
        }
    }

    public void UpdateFromSamples(
        IReadOnlyList<TrendSampleEntity> samples,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var localStart = windowStart.LocalDateTime;
        var localEnd = windowEnd.LocalDateTime;
        var nextPoints = new ObservableCollection<DateTimePoint>();
        foreach (var sample in samples)
        {
            var point = TrendSampleMapper.TryToPoint(sample);
            if (point == null)
            {
                continue;
            }

            var localTime = point.Timestamp.LocalDateTime;
            if (localTime < localStart || localTime > localEnd)
            {
                continue;
            }

            nextPoints.Add(new DateTimePoint(localTime, point.Value));
        }

        Points.Clear();
        foreach (var nextPoint in nextPoints)
        {
            Points.Add(nextPoint);
        }
    }

    private static SKColor? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var normalized = hex.Trim();
        if (!normalized.StartsWith('#'))
        {
            normalized = $"#{normalized}";
        }

        return SKColor.TryParse(normalized, out var color) ? color : null;
    }
}
