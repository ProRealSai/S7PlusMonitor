using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>变量属性展示格式化：地址、维度、Hex CRC 等。</summary>

public static class VariableDisplayFormatter
{
    public static string FormatHex(uint value) => $"0x{value:X8} ({value})";

    public static string FormatBool(bool value) => value ? "TRUE" : "FALSE";

    public static string FormatArrayDimensions(IReadOnlyList<ArrayDimensionInfo>? dimensions)
    {
        if (dimensions == null || dimensions.Count == 0)
        {
            return "无";
        }

        return string.Join("; ", dimensions.Select((dimension, index) =>
            $"维{index}: [{dimension.LowerBound}..{dimension.UpperBound}] ({dimension.ElementCount} 个元素)"));
    }
}
