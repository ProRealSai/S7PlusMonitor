using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.ViewModels;

/// <summary>变量属性明细行（键值对展示）。</summary>

public sealed class VariablePropertyItemViewModel
{
    public VariablePropertyItemViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    public static IEnumerable<VariablePropertyItemViewModel> FromVariable(VariableInfo variable)
    {
        yield return new VariablePropertyItemViewModel("符号名称", variable.Symbol);
        yield return new VariablePropertyItemViewModel("访问序列", variable.Address);
        yield return new VariablePropertyItemViewModel("符号 CRC", VariableDisplayFormatter.FormatHex(variable.SymbolCrc));
        yield return new VariablePropertyItemViewModel("数据类型", variable.DataType);
        yield return new VariablePropertyItemViewModel("数据类型 ID", variable.SoftdatatypeId.ToString());
        yield return new VariablePropertyItemViewModel("优化地址", VariableDisplayFormatter.FormatHex(variable.OptAddress));
        yield return new VariablePropertyItemViewModel("优化位偏移", variable.OptBitoffset.ToString());
        yield return new VariablePropertyItemViewModel("非优化地址", VariableDisplayFormatter.FormatHex(variable.NonOptAddress));
        yield return new VariablePropertyItemViewModel("非优化位偏移", variable.NonOptBitoffset.ToString());
        yield return new VariablePropertyItemViewModel("HMI 可见", VariableDisplayFormatter.FormatBool(variable.HmiVisible));
        yield return new VariablePropertyItemViewModel("HMI 可访问", VariableDisplayFormatter.FormatBool(variable.HmiAccessible));
        yield return new VariablePropertyItemViewModel("数组元素数", variable.ArrayElementCount.ToString());
        yield return new VariablePropertyItemViewModel("数组维度", VariableDisplayFormatter.FormatArrayDimensions(variable.ArrayDimensions));
    }
}
