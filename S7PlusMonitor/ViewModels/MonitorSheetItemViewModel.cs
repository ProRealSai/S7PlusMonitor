using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.ViewModels;

/// <summary>监视表单行 VM：格式化当前值、强制输入、趋势状态与行操作。</summary>

public partial class MonitorSheetItemViewModel : ObservableObject
{
    private object? _rawValue;
    private VariableReadQuality _quality = VariableReadQuality.Bad;
    private bool _suppressFormatEvents;

    public MonitorSheetItemViewModel(MonitorSheetItemEntity entity)
    {
        ItemId = entity.Id;
        Symbol = entity.Symbol;
        DataType = entity.DataType ?? string.Empty;
        _suppressFormatEvents = true;
        _displayFormat = DisplayFormatRegistry.ParseFormatOrDefault(entity.DisplayFormat, entity.DataType).ToString();
        RefreshAvailableFormats();
        RefreshForceUiState();
        _suppressFormatEvents = false;
    }

    public int ItemId { get; }

    public int Index { get; init; }

    public string IndexText => Index.ToString();

    [ObservableProperty]
    private string _symbol;

    [ObservableProperty]
    private string _dataType;

    [ObservableProperty]
    private string _displayFormat;

    [ObservableProperty]
    private string _currentValue = "—";

    [ObservableProperty]
    private string _forceValueInput = string.Empty;

    [ObservableProperty]
    private string _forcePlaceholder = string.Empty;

    [ObservableProperty]
    private bool _isBoolForceMode;

    [ObservableProperty]
    private bool _isForceInputVisible;

    [ObservableProperty]
    private bool _isInTrend;

    [ObservableProperty]
    private bool _isSymbolMissing;

    public string SymbolDisplay => IsSymbolMissing ? $"⚠ {Symbol}" : Symbol;

    public bool IsForceColumnVisible { get; } = true;

    public ObservableCollection<string> AvailableFormats { get; } = [];

    public Func<string, Task>? DisplayFormatChangedHandler { get; init; }

    public Func<Task>? ForceWriteHandler { get; init; }

    public Func<Task>? ToggleBoolHandler { get; init; }

    public Func<Task>? ToggleTrendHandler { get; init; }

    public Func<Task>? RemoveHandler { get; init; }

    public bool IsNotInTrend => !IsInTrend;

    public string TrendActionText => IsInTrend ? "删除趋势" : "加入趋势";

    public string TrendActionToolTip => IsInTrend
        ? "从趋势图中移除此变量（保留监视行）"
        : "将此变量加入当前连接的趋势图";

    public bool HasGoodValue => _quality == VariableReadQuality.Good && _rawValue != null;

    public object? RawValue => _rawValue;

    partial void OnDisplayFormatChanged(string value)
    {
        RefreshForceUiState();
        RefreshFormattedValue();
        if (!_suppressFormatEvents && DisplayFormatChangedHandler != null)
        {
            _ = DisplayFormatChangedHandler(value);
        }
    }

    partial void OnIsInTrendChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotInTrend));
        OnPropertyChanged(nameof(TrendActionText));
        OnPropertyChanged(nameof(TrendActionToolTip));
    }

    partial void OnIsSymbolMissingChanged(bool value) => OnPropertyChanged(nameof(SymbolDisplay));

    public void ApplyReadResult(VariableReadResult result)
    {
        _quality = result.Quality;
        _rawValue = result.Quality == VariableReadQuality.Good ? result.Value : null;
        RefreshFormattedValue();
    }

    public void RefreshFormattedValue()
    {
        CurrentValue = _quality != VariableReadQuality.Good || _rawValue == null
            ? "—"
            : ValueDisplayFormatter.Format(_rawValue, DisplayFormat, DataType);
    }

    private void RefreshAvailableFormats()
    {
        AvailableFormats.Clear();
        foreach (var formatId in DisplayFormatRegistry.GetAllowedFormatIds(DataType))
        {
            AvailableFormats.Add(formatId);
        }

        if (!AvailableFormats.Contains(DisplayFormat))
        {
            _suppressFormatEvents = true;
            DisplayFormat = DisplayFormatRegistry.GetDefaultFormatId(DataType);
            _suppressFormatEvents = false;
        }
    }

    private void RefreshForceUiState()
    {
        IsBoolForceMode = DisplayFormatRegistry.UsesBoolForceMode(DataType, DisplayFormat);
        IsForceInputVisible = !IsBoolForceMode;
        ForcePlaceholder = ValueDisplayFormatter.GetForcePlaceholder(DisplayFormat, DataType);
    }

    [RelayCommand]
    private Task ToggleTrendAsync() =>
        ToggleTrendHandler?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task RemoveAsync() =>
        RemoveHandler?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ForceWriteAsync() =>
        ForceWriteHandler?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ToggleBoolAsync() =>
        ToggleBoolHandler?.Invoke() ?? Task.CompletedTask;
}
