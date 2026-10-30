using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.ViewModels;

/// <summary>浏览列表行 VM，预计算 <see cref="SearchText"/> 以加速 50k 过滤。</summary>
public partial class VariableListItemViewModel : ObservableObject
{
    public VariableListItemViewModel(int index, VariableInfo source)
    {
        Index = index;
        Source = source;
        SearchText = BuildSearchText(source);
    }

    public int Index { get; }

    public VariableInfo Source { get; }

    /// <summary>小写拼接的搜索文本：符号/类型/地址/Crc/维度，避免过滤时重复 Format。</summary>
    public string SearchText { get; }

    public string IndexText => Index.ToString();

    public string Symbol => Source.Symbol;

    public string DataType => Source.DataType;

    private static string BuildSearchText(VariableInfo source)
    {
        var parts = new[]
        {
            source.Symbol,
            source.DataType,
            source.Address,
            VariableDisplayFormatter.FormatHex(source.SymbolCrc),
            VariableDisplayFormatter.FormatArrayDimensions(source.ArrayDimensions)
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
    }

    public Func<Task>? AddToMonitorHandler { get; init; }

    [RelayCommand]
    private Task AddToMonitorAsync() =>
        AddToMonitorHandler?.Invoke() ?? Task.CompletedTask;
}
