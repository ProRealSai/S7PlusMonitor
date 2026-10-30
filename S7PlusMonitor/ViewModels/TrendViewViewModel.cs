using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;
using SkiaSharp;

namespace S7PlusMonitor.ViewModels;

/// <summary>单连接默认趋势图的 LiveCharts2 状态。</summary>
public partial class TrendViewViewModel : ObservableObject
{
    private readonly List<TrendSeriesViewModel> _series = [];
    private TrendViewEntity _settings;

    public TrendViewViewModel(TrendViewEntity settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ViewId = settings.Id;

        XAxes =
        [
            CreateDateTimeAxis(settings, isHistoryView: false)
        ];
        YAxes =
        [
            CreateValueAxis(settings, isHistoryView: false)
        ];
    }

    public int ViewId { get; }

    public TrendXRangeMode XRangeMode => _settings.XRangeMode;

    public int RollingWindowSeconds => IsChartPaused
        ? TrendRangeHelper.ClampHistoryRollingWindow(_settings.HistoryRollingWindowSeconds)
        : TrendRangeHelper.ClampLiveRollingWindow(_settings.LiveRollingWindowSeconds);

    public bool CurrentYAutoScale => TrendRangeHelper.GetYRange(_settings, IsChartPaused).AutoScale;

    public bool IsChartPaused { get; private set; }

    public ZoomAndPanMode ChartZoomMode =>
        IsChartPaused
            ? ZoomAndPanMode.Both | ZoomAndPanMode.NoFit
            : ZoomAndPanMode.None;

    public bool CanResetChartView => IsChartPaused;

    public ObservableCollection<ISeries> ChartSeries { get; } = [];

    public Axis[] XAxes { get; private set; }

    public Axis[] YAxes { get; private set; }

    public bool HasChartSeries => ChartSeries.Count > 0;

    public string EmptyHint =>
        "📈 趋势图暂无曲线 — 在监视表行点击「加入趋势」添加变量";

    public string StatusHint
    {
        get
        {
            if (!HasChartSeries)
            {
                return "从监视表加入变量后显示历史曲线";
            }

            var rangeText = IsChartPaused
                ? (XRangeMode == TrendXRangeMode.Rolling
                    ? $"历史 · 最近 {TrendRangeHelper.FormatHistoryRollingWindow(RollingWindowSeconds)}"
                    : $"历史 · {DescribeAbsoluteRange()}")
                : $"实时 · 最近 {TrendRangeHelper.FormatLiveRollingWindow(RollingWindowSeconds)}";
            var yRange = TrendRangeHelper.GetYRange(_settings, IsChartPaused);
            var yText = yRange.AutoScale
                ? "Y 自动"
                : $"Y [{FormatAxisValue(yRange.Min)}–{FormatAxisValue(yRange.Max)}]";
            var pauseText = IsChartPaused ? " · 已暂停 · 可缩放平移" : string.Empty;
            return $"{rangeText} · {yText} · {ChartSeries.Count} 条曲线{pauseText}";
        }
    }

    public void ApplySettings(TrendViewEntity settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        XAxes = [CreateDateTimeAxis(settings, IsChartPaused)];
        YAxes = [CreateValueAxis(settings, IsChartPaused)];
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
        OnPropertyChanged(nameof(XRangeMode));
        OnPropertyChanged(nameof(RollingWindowSeconds));
        OnPropertyChanged(nameof(CurrentYAutoScale));
        NotifyChartStateChanged();
    }

    public void SetChartPaused(bool paused)
    {
        if (IsChartPaused == paused)
        {
            return;
        }

        IsChartPaused = paused;
        OnPropertyChanged(nameof(ChartZoomMode));
        OnPropertyChanged(nameof(CanResetChartView));
        XAxes = [CreateDateTimeAxis(_settings, IsChartPaused)];
        YAxes = [CreateValueAxis(_settings, IsChartPaused)];
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
        UpdateAxes(DateTimeOffset.UtcNow);

        OnPropertyChanged(nameof(RollingWindowSeconds));
        OnPropertyChanged(nameof(CurrentYAutoScale));
        NotifyChartStateChanged();
    }

    public void ResetChartView()
    {
        if (IsChartPaused && XAxes[0] is DateTimeAxis dateAxis)
        {
            dateAxis.MinLimit = null;
            dateAxis.MaxLimit = null;
        }

        UpdateAxes(DateTimeOffset.UtcNow);
    }

    public (DateTimeOffset Start, DateTimeOffset End) GetVisibleRange(DateTimeOffset now) =>
        GetChartVisibleRange(now);

    public (DateTimeOffset Start, DateTimeOffset End) GetChartVisibleRange(DateTimeOffset now) =>
        IsChartPaused
            ? TrendRangeHelper.GetVisibleRange(_settings, now)
            : TrendRangeHelper.GetLiveVisibleRange(_settings, now);

    public int GetRetentionSeconds() => TrendRangeHelper.GetRetentionSeconds(_settings);

    public int GetSampleIntervalMs() => TrendRangeHelper.ClampSampleIntervalMs(_settings.SampleIntervalMs);

    public void LoadSeries(
        IReadOnlyList<TrendViewItemEntity> viewItems,
        IReadOnlyDictionary<int, MonitorSheetItemEntity> monitorItems,
        IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> samplesByItemId)
    {
        _series.Clear();
        ChartSeries.Clear();

        var now = DateTimeOffset.UtcNow;
        var (_, end) = GetVisibleRange(now);
        var colorIndex = 0;
        foreach (var viewItem in viewItems.OrderBy(item => item.SortOrder))
        {
            if (!monitorItems.TryGetValue(viewItem.ItemId, out var monitorItem))
            {
                continue;
            }

            var series = new TrendSeriesViewModel(
                viewItem.ItemId,
                monitorItem.Symbol,
                colorIndex++,
                viewItem.DisplayColor);
            var samples = samplesByItemId.TryGetValue(viewItem.ItemId, out var itemSamples)
                ? itemSamples
                : [];
            series.LoadFromSamples(samples);
            _series.Add(series);
            ChartSeries.Add(series.Series);
        }

        UpdateAxes(now);
        NotifyChartStateChanged();
    }

    public void RefreshFromSamples(IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> samplesByItemId)
    {
        if (_series.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (start, end) = GetChartVisibleRange(now);
        foreach (var series in _series)
        {
            var samples = samplesByItemId.TryGetValue(series.ItemId, out var itemSamples)
                ? itemSamples
                : [];
            if (series.Points.Count == 0)
            {
                series.UpdateFromSamples(samples, start, end);
            }
            else
            {
                series.AppendPendingSamples(samples, start);
            }
        }

        UpdateAxes(now);
        NotifyChartStateChanged();
    }

    /// <summary>历史/实时增量：仅追加尚未绘制的采样点（与实时轮询相同路径）。</summary>
    public void AppendIncrementalSamples(IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> samplesByItemId)
    {
        RefreshFromLiveSamples(samplesByItemId);
    }

    /// <summary>实时播放：追加缓冲中所有尚未绘制的采样点。</summary>
    public void RefreshFromLiveSamples(IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> samplesByItemId)
    {
        if (_series.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (start, _) = GetChartVisibleRange(now);
        foreach (var series in _series)
        {
            if (!samplesByItemId.TryGetValue(series.ItemId, out var samples) || samples.Count == 0)
            {
                continue;
            }

            series.AppendPendingSamples(samples, start);
        }

        UpdateAxes(now);
        NotifyChartStateChanged();
    }

    private void UpdateAxes(DateTimeOffset now)
    {
        var (start, end) = GetChartVisibleRange(now);
        var spanSeconds = Math.Max(1, (int)(end - start).TotalSeconds);

        if (XAxes[0] is DateTimeAxis dateAxis)
        {
            if (!IsChartPaused)
            {
                // 轴跟随实际数据：实时缓冲上限 600 点/变量，快速采样(10ms)下仅覆盖数秒，
                // 若强制使用完整配置窗口，波形会被压缩到右缘呈乱线（混叠假象）
                var oldestTicks = GetOldestPointTicks();
                var axisStartTicks = oldestTicks.HasValue
                    ? Math.Max(start.LocalDateTime.Ticks, oldestTicks.Value)
                    : start.LocalDateTime.Ticks;
                dateAxis.MinLimit = axisStartTicks;
                dateAxis.MaxLimit = end.LocalDateTime.Ticks;
                var dataSpanSeconds = Math.Max(
                    1,
                    (int)((end.LocalDateTime.Ticks - axisStartTicks) / TimeSpan.TicksPerSecond));
                SyncLiveAxisLabelStep(dateAxis, dataSpanSeconds);
            }
            else if (dateAxis.MinLimit is not double min
                     || dateAxis.MaxLimit is not double max
                     || max <= min)
            {
                dateAxis.MinLimit = start.LocalDateTime.Ticks;
                dateAxis.MaxLimit = end.LocalDateTime.Ticks;
                SyncHistoryAxisLabelStep(dateAxis, spanSeconds);
            }
            else
            {
                SyncHistoryAxisLabelStep(dateAxis, useCurrentLimits: true);
            }
        }

        if (YAxes[0] is Axis valueAxis)
        {
            var (autoScale, yMin, yMax) = TrendRangeHelper.GetYRange(_settings, IsChartPaused);
            if (autoScale)
            {
                valueAxis.MinLimit = null;
                valueAxis.MaxLimit = null;
            }
            else if (TrendRangeHelper.IsValidManualYRange(yMin, yMax))
            {
                valueAxis.MinLimit = yMin;
                valueAxis.MaxLimit = yMax;
            }
        }
    }

    private void SyncHistoryAxisLabelStep(DateTimeAxis dateAxis, int spanSeconds)
    {
        ApplyAxisLabelStep(dateAxis, TrendRangeHelper.ComputeAxisLabelStepSeconds(spanSeconds));
    }

    private static void SyncLiveAxisLabelStep(DateTimeAxis dateAxis, int spanSeconds)
    {
        var labelStep = TrendRangeHelper.ComputeLiveAxisLabelStepSeconds(spanSeconds);
        ApplyAxisLabelStep(dateAxis, labelStep);
    }

    private static void ApplyAxisLabelStep(DateTimeAxis dateAxis, double labelStepSeconds)
    {
        var stepTicks = TimeSpan.FromSeconds(labelStepSeconds).Ticks;
        dateAxis.MinStep = stepTicks;
        dateAxis.UnitWidth = stepTicks;
    }

    /// <summary>取全部曲线最早点时间（Ticks）；实时轴用它收缩窗口以匹配缓冲实际覆盖范围。</summary>
    private double? GetOldestPointTicks()
    {
        double? oldest = null;
        foreach (var series in _series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            var ticks = series.Points[0].DateTime.Ticks;
            if (oldest == null || ticks < oldest)
            {
                oldest = ticks;
            }
        }

        return oldest;
    }

    private void SyncHistoryAxisLabelStep(DateTimeAxis dateAxis, bool useCurrentLimits)
    {
        if (useCurrentLimits
            && dateAxis.MinLimit is double min
            && dateAxis.MaxLimit is double max
            && max > min)
        {
            var spanSeconds = Math.Max(1, (int)((max - min) / TimeSpan.TicksPerSecond));
            SyncHistoryAxisLabelStep(dateAxis, spanSeconds);
            return;
        }

        SyncHistoryAxisLabelStep(dateAxis, AppConstants.DefaultHistoryRollingWindowSeconds);
    }

    private static SolidColorPaint CreateAxisLabelPaint() =>
        new(SKColor.Parse("#64748B"));

    private static SolidColorPaint CreateAxisSeparatorPaint() =>
        new(SKColor.Parse("#E2E8F0"))
        {
            StrokeThickness = 1
        };

    private DateTimeAxis CreateDateTimeAxis(TrendViewEntity settings, bool isHistoryView)
    {
        var spanSeconds = isHistoryView && settings.XRangeMode == TrendXRangeMode.Rolling
            ? TrendRangeHelper.ClampHistoryRollingWindow(settings.HistoryRollingWindowSeconds)
            : isHistoryView && settings.XRangeMode == TrendXRangeMode.Absolute
                ? TrendRangeHelper.GetRetentionSeconds(settings)
                : TrendRangeHelper.ClampLiveRollingWindow(settings.LiveRollingWindowSeconds);

        var labelStep = isHistoryView
            ? TrendRangeHelper.ComputeAxisLabelStepSeconds(spanSeconds)
            : TrendRangeHelper.ComputeLiveAxisLabelStepSeconds(spanSeconds);

        if (isHistoryView)
        {
            return new DateTimeAxis(
                TimeSpan.FromSeconds(labelStep),
                TrendRangeHelper.FormatChartAxisDateTime)
            {
                MinStep = TimeSpan.FromSeconds(labelStep).Ticks,
                AnimationsSpeed = TimeSpan.Zero,
                LabelsPaint = CreateAxisLabelPaint(),
                SeparatorsPaint = CreateAxisSeparatorPaint(),
                TextSize = 11,
                ShowSeparatorLines = true
            };
        }

        return new DateTimeAxis(
            TimeSpan.FromSeconds(labelStep),
            TrendRangeHelper.FormatChartAxisDateTime)
        {
            MinStep = TimeSpan.FromSeconds(labelStep).Ticks,
            AnimationsSpeed = TimeSpan.Zero,
            LabelsPaint = CreateAxisLabelPaint(),
            SeparatorsPaint = CreateAxisSeparatorPaint(),
            TextSize = 11,
            ShowSeparatorLines = true
        };
    }

    private static Axis CreateValueAxis(TrendViewEntity settings, bool isHistoryView)
    {
        var axis = new Axis
        {
            AnimationsSpeed = TimeSpan.Zero,
            LabelsPaint = CreateAxisLabelPaint(),
            SeparatorsPaint = CreateAxisSeparatorPaint(),
            TextSize = 11,
            ShowSeparatorLines = true
        };

        var (autoScale, yMin, yMax) = TrendRangeHelper.GetYRange(settings, isHistoryView);
        if (!autoScale && TrendRangeHelper.IsValidManualYRange(yMin, yMax))
        {
            axis.MinLimit = yMin;
            axis.MaxLimit = yMax;
        }

        return axis;
    }

    private string DescribeAbsoluteRange()
    {
        var start = TrendRangeHelper.ParseUtc(_settings.XRangeStartUtc)?.ToLocalTime();
        if (start == null)
        {
            return "绝对时间";
        }

        if (_settings.XRangeEndFollowsNow)
        {
            return $"{TrendRangeHelper.FormatChartAxisDateTime(start.Value)} → 现在";
        }

        var end = TrendRangeHelper.ParseUtc(_settings.XRangeEndUtc)?.ToLocalTime();
        return end == null
            ? $"{TrendRangeHelper.FormatChartAxisDateTime(start.Value)} → …"
            : $"{TrendRangeHelper.FormatChartAxisDateTime(start.Value)} → {TrendRangeHelper.FormatChartAxisDateTime(end.Value)}";
    }

    private static string FormatAxisValue(double? value) =>
        value?.ToString("0.##") ?? "?";

    private void NotifyChartStateChanged()
    {
        OnPropertyChanged(nameof(HasChartSeries));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(StatusHint));
    }
}
