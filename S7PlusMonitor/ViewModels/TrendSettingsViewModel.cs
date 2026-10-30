using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.ViewModels;

/// <summary>趋势设置弹窗 VM：校验采样间隔 ≥ 监视周期，窗口与轴配置。</summary>

public partial class TrendSettingsViewModel : ObservableObject
{
    private readonly ITrendViewRepository _trendViewRepository;
    private readonly TrendViewEntity _entity;
    private readonly string? _connectionProfileName;
    private readonly int _pollIntervalMs;

    public TrendSettingsViewModel(
        TrendViewEntity entity,
        ITrendViewRepository trendViewRepository,
        string? connectionProfileName = null,
        int pollIntervalMs = AppConstants.DefaultPollIntervalMs)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _trendViewRepository = trendViewRepository ?? throw new ArgumentNullException(nameof(trendViewRepository));
        _connectionProfileName = connectionProfileName;
        _pollIntervalMs = PollIntervalHelper.Clamp(pollIntervalMs);

        XRangeModeOptions =
        [
            new TrendXRangeModeOption(TrendXRangeMode.Rolling, "滚动时长（相对当前）"),
            new TrendXRangeModeOption(TrendXRangeMode.Absolute, "绝对时间（起止时刻）")
        ];

        SelectedXRangeMode = XRangeModeOptions.First(option => option.Mode == entity.XRangeMode);

        var liveSeconds = TrendRangeHelper.ClampLiveRollingWindow(entity.LiveRollingWindowSeconds);
        var (liveValue, liveUnit) = TrendRangeHelper.DecomposeLiveRollingSeconds(liveSeconds);
        LiveRollingWindowValueText = liveValue.ToString();
        SelectedLiveRollingWindowUnit = LiveRollingWindowUnitOptions.First(option => option.Unit == liveUnit);

        var historySeconds = TrendRangeHelper.ClampHistoryRollingWindow(entity.HistoryRollingWindowSeconds);
        var (historyValue, historyUnit) = TrendRangeHelper.DecomposeHistoryRollingSeconds(historySeconds);
        HistoryRollingWindowValueText = historyValue.ToString();
        SelectedHistoryRollingWindowUnit = HistoryRollingWindowUnitOptions.First(option => option.Unit == historyUnit);

        SampleIntervalMsText = TrendRangeHelper.ClampSampleIntervalMs(
            entity.SampleIntervalMs,
            _pollIntervalMs).ToString();

        LiveYAutoScale = entity.LiveYAutoScale;
        LiveYMinText = entity.LiveYMin?.ToString("0.####") ?? string.Empty;
        LiveYMaxText = entity.LiveYMax?.ToString("0.####") ?? string.Empty;

        HistoryYAutoScale = entity.HistoryYAutoScale;
        HistoryYMinText = entity.HistoryYMin?.ToString("0.####") ?? string.Empty;
        HistoryYMaxText = entity.HistoryYMax?.ToString("0.####") ?? string.Empty;

        XRangeEndFollowsNow = entity.XRangeEndFollowsNow;

        var defaultStart = DateTimeOffset.UtcNow.AddSeconds(-historySeconds).ToLocalTime();
        var start = TrendRangeHelper.ParseUtc(entity.XRangeStartUtc)?.ToLocalTime() ?? defaultStart;
        var end = TrendRangeHelper.ParseUtc(entity.XRangeEndUtc)?.ToLocalTime()
                  ?? DateTimeOffset.Now.ToLocalTime();

        XRangeStartDate = start.Date;
        XRangeStartTime = start.TimeOfDay;
        XRangeEndDate = end.Date;
        XRangeEndTime = end.TimeOfDay;
    }

    public IReadOnlyList<TrendXRangeModeOption> XRangeModeOptions { get; }

    [ObservableProperty]
    private TrendXRangeModeOption _selectedXRangeMode = null!;

    public IReadOnlyList<TrendDurationUnitOption> LiveRollingWindowUnitOptions { get; } =
        TrendDurationUnitOption.LiveRollingWindowUnits;

    public IReadOnlyList<TrendDurationUnitOption> HistoryRollingWindowUnitOptions { get; } =
        TrendDurationUnitOption.All;

    [ObservableProperty]
    private string _liveRollingWindowValueText = "5";

    [ObservableProperty]
    private TrendDurationUnitOption _selectedLiveRollingWindowUnit = null!;

    [ObservableProperty]
    private string _historyRollingWindowValueText = "5";

    [ObservableProperty]
    private TrendDurationUnitOption _selectedHistoryRollingWindowUnit = null!;

    [ObservableProperty]
    private string _sampleIntervalMsText = AppConstants.DefaultSampleIntervalMs.ToString();

    [ObservableProperty]
    private bool _liveYAutoScale = true;

    [ObservableProperty]
    private string _liveYMinText = string.Empty;

    [ObservableProperty]
    private string _liveYMaxText = string.Empty;

    [ObservableProperty]
    private bool _historyYAutoScale = true;

    [ObservableProperty]
    private string _historyYMinText = string.Empty;

    [ObservableProperty]
    private string _historyYMaxText = string.Empty;

    [ObservableProperty]
    private bool _xRangeEndFollowsNow = true;

    [ObservableProperty]
    private DateTime _xRangeStartDate;

    [ObservableProperty]
    private TimeSpan _xRangeStartTime;

    [ObservableProperty]
    private DateTime _xRangeEndDate;

    [ObservableProperty]
    private TimeSpan _xRangeEndTime;

    [ObservableProperty]
    private string? _validationMessage;

    public string WindowSubtitle =>
        string.IsNullOrWhiteSpace(_connectionProfileName)
            ? "当前连接默认趋势图"
            : $"档案「{_connectionProfileName}」· 设置仅对本连接生效";

    public bool IsRollingMode => SelectedXRangeMode.Mode == TrendXRangeMode.Rolling;

    public bool IsAbsoluteMode => SelectedXRangeMode.Mode == TrendXRangeMode.Absolute;

    public bool ShowLiveManualYRange => !LiveYAutoScale;

    public bool ShowHistoryManualYRange => !HistoryYAutoScale;

    public string LiveYScaleModeText => LiveYAutoScale ? "自动缩放模式" : "最大最小值模式";

    public string HistoryYScaleModeText => HistoryYAutoScale ? "自动缩放模式" : "最大最小值模式";

    public bool ShowFixedEndTime => IsAbsoluteMode && !XRangeEndFollowsNow;

    public string SampleIntervalHint =>
        $"{TrendRangeHelper.FormatSampleIntervalRule(_pollIntervalMs)}；范围 {TrendRangeHelper.SampleIntervalRangeHint}。";

    public string LiveViewHint =>
        $"播放中按监视周期刷新；滚动时长 {TrendRangeHelper.LiveRollingWindowRangeHint}；X 轴时间精确到毫秒。";

    public string HistoryViewHint =>
        $"暂停后从 SQLite 加载；滚动时长 {TrendRangeHelper.HistoryRollingWindowRangeHint}。";

    public string HistoryXAxisHint =>
        IsRollingMode
            ? HistoryViewHint
            : "暂停后从 SQLite 加载；可指定起止时间，或勾选结束时间跟随当前。";

    public TrendViewEntity? SavedEntity { get; private set; }

    public bool WasSaved { get; private set; }

    public event EventHandler<bool>? CloseRequested;

    partial void OnSelectedXRangeModeChanged(TrendXRangeModeOption value)
    {
        OnPropertyChanged(nameof(IsRollingMode));
        OnPropertyChanged(nameof(IsAbsoluteMode));
        OnPropertyChanged(nameof(ShowFixedEndTime));
        OnPropertyChanged(nameof(HistoryXAxisHint));
    }

    partial void OnLiveYAutoScaleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLiveManualYRange));
        OnPropertyChanged(nameof(LiveYScaleModeText));
    }

    partial void OnHistoryYAutoScaleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHistoryManualYRange));
        OnPropertyChanged(nameof(HistoryYScaleModeText));
    }

    partial void OnXRangeEndFollowsNowChanged(bool value) => OnPropertyChanged(nameof(ShowFixedEndTime));

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationMessage = null;

        if (!int.TryParse(LiveRollingWindowValueText.Trim(), out var liveRollingWindowAmount)
            || liveRollingWindowAmount <= 0)
        {
            ValidationMessage = "实时滚动时长须为正整数。";
            return;
        }

        var liveRollingWindowSeconds = TrendRangeHelper.ToSeconds(
            liveRollingWindowAmount,
            SelectedLiveRollingWindowUnit.Unit);
        if (!TrendRangeHelper.IsValidLiveRollingWindow(liveRollingWindowSeconds))
        {
            ValidationMessage = $"实时滚动时长须在 {TrendRangeHelper.LiveRollingWindowRangeHint} 之间。";
            return;
        }

        liveRollingWindowSeconds = TrendRangeHelper.ClampLiveRollingWindow(liveRollingWindowSeconds);

        if (!int.TryParse(HistoryRollingWindowValueText.Trim(), out var historyRollingWindowAmount)
            || historyRollingWindowAmount <= 0)
        {
            ValidationMessage = "历史滚动时长须为正整数。";
            return;
        }

        var historyRollingWindowSeconds = TrendRangeHelper.ToSeconds(
            historyRollingWindowAmount,
            SelectedHistoryRollingWindowUnit.Unit);
        if (!TrendRangeHelper.IsValidHistoryRollingWindow(historyRollingWindowSeconds))
        {
            ValidationMessage = $"历史滚动时长须在 {TrendRangeHelper.HistoryRollingWindowRangeHint} 之间。";
            return;
        }

        historyRollingWindowSeconds = TrendRangeHelper.ClampHistoryRollingWindow(historyRollingWindowSeconds);

        if (!int.TryParse(SampleIntervalMsText.Trim(), out var sampleIntervalMs)
            || !TrendRangeHelper.IsValidSampleIntervalMs(sampleIntervalMs, _pollIntervalMs))
        {
            ValidationMessage = $"历史采样间隔须在 {TrendRangeHelper.SampleIntervalRangeHint} 之间，且 {TrendRangeHelper.FormatSampleIntervalRule(_pollIntervalMs)}。";
            return;
        }

        sampleIntervalMs = TrendRangeHelper.ClampSampleIntervalMs(sampleIntervalMs, _pollIntervalMs);

        double? liveYMin = null;
        double? liveYMax = null;
        if (!LiveYAutoScale)
        {
            if (!double.TryParse(LiveYMinText, out var parsedMin) || !double.TryParse(LiveYMaxText, out var parsedMax))
            {
                ValidationMessage = "实时 Y 轴须填写有效数值。";
                return;
            }

            if (!TrendRangeHelper.IsValidManualYRange(parsedMin, parsedMax))
            {
                ValidationMessage = "实时 Y 轴下限须小于上限。";
                return;
            }

            liveYMin = parsedMin;
            liveYMax = parsedMax;
        }

        double? historyYMin = null;
        double? historyYMax = null;
        if (!HistoryYAutoScale)
        {
            if (!double.TryParse(HistoryYMinText, out var parsedMin) || !double.TryParse(HistoryYMaxText, out var parsedMax))
            {
                ValidationMessage = "历史 Y 轴须填写有效数值。";
                return;
            }

            if (!TrendRangeHelper.IsValidManualYRange(parsedMin, parsedMax))
            {
                ValidationMessage = "历史 Y 轴下限须小于上限。";
                return;
            }

            historyYMin = parsedMin;
            historyYMax = parsedMax;
        }

        DateTimeOffset? absoluteStart = null;
        DateTimeOffset? absoluteEnd = null;
        if (SelectedXRangeMode.Mode == TrendXRangeMode.Absolute)
        {
            absoluteStart = CombineLocalDateTime(XRangeStartDate, XRangeStartTime);
            absoluteEnd = XRangeEndFollowsNow
                ? null
                : CombineLocalDateTime(XRangeEndDate, XRangeEndTime);

            if (!TrendRangeHelper.IsValidAbsoluteRange(absoluteStart, absoluteEnd, XRangeEndFollowsNow))
            {
                ValidationMessage = XRangeEndFollowsNow
                    ? "请设置有效的开始时间。"
                    : "结束时间须不早于开始时间。";
                return;
            }
        }

        _entity.XRangeMode = SelectedXRangeMode.Mode;
        _entity.LiveRollingWindowSeconds = liveRollingWindowSeconds;
        _entity.HistoryRollingWindowSeconds = historyRollingWindowSeconds;
        _entity.SampleIntervalMs = sampleIntervalMs;
        _entity.LiveYAutoScale = LiveYAutoScale;
        _entity.LiveYMin = liveYMin;
        _entity.LiveYMax = liveYMax;
        _entity.HistoryYAutoScale = HistoryYAutoScale;
        _entity.HistoryYMin = historyYMin;
        _entity.HistoryYMax = historyYMax;
        _entity.XRangeEndFollowsNow = XRangeEndFollowsNow;
        _entity.XRangeStartUtc = SelectedXRangeMode.Mode == TrendXRangeMode.Absolute && absoluteStart != null
            ? TrendRangeHelper.FormatUtc(absoluteStart.Value)
            : null;
        _entity.XRangeEndUtc = SelectedXRangeMode.Mode == TrendXRangeMode.Absolute && !XRangeEndFollowsNow && absoluteEnd != null
            ? TrendRangeHelper.FormatUtc(absoluteEnd.Value)
            : null;

        await _trendViewRepository.UpdateViewSettingsAsync(_entity).ConfigureAwait(true);
        SavedEntity = _entity;
        WasSaved = true;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private static DateTimeOffset CombineLocalDateTime(DateTime date, TimeSpan time)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}

public sealed record TrendXRangeModeOption(TrendXRangeMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record TrendDurationUnitOption(TrendDurationUnit Unit, string Label)
{
    public static IReadOnlyList<TrendDurationUnitOption> All { get; } =
    [
        new(TrendDurationUnit.Day, "天"),
        new(TrendDurationUnit.Hour, "小时"),
        new(TrendDurationUnit.Minute, "分钟"),
        new(TrendDurationUnit.Second, "秒")
    ];

    public static IReadOnlyList<TrendDurationUnitOption> LiveRollingWindowUnits { get; } =
    [
        new(TrendDurationUnit.Second, "秒"),
        new(TrendDurationUnit.Minute, "分钟")
    ];

    public override string ToString() => Label;
}
