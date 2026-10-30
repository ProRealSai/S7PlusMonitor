namespace S7PlusMonitor.Core.Models;

/// <summary>PLC 变量数组维度（单维）：下界/元素数/上界。</summary>
public sealed class ArrayDimensionInfo
{
    public int LowerBound { get; init; }

    public uint ElementCount { get; init; }

    public long UpperBound { get; init; }

    public override string ToString() =>
        $"[{LowerBound}..{UpperBound}] ({ElementCount} elements)";
}

/// <summary>浏览得到的 PLC 变量元数据；由 <c>S7CommPlusPlcService.MapVariable(VarInfo)</c> 映射而来。</summary>
public sealed class VariableInfo
{
    /// <summary>PLC 符号路径（TIA 变量名，如 "DB1.MyVar"）。</summary>
    public required string Symbol { get; init; }

    /// <summary>前端展示类型（英文，如 "Word"/"DWord"/"Real"）。</summary>
    public required string DataType { get; init; }

    /// <summary>访问序列/地址文本（来自 VarInfo.AccessSequence）。</summary>
    public required string Address { get; init; }

    public uint SymbolCrc { get; init; }

    public uint SoftdatatypeId { get; init; }

    public uint OptAddress { get; init; }

    public int OptBitoffset { get; init; }

    public uint NonOptAddress { get; init; }

    public int NonOptBitoffset { get; init; }

    public bool HmiVisible { get; init; }

    public bool HmiAccessible { get; init; }

    public uint ArrayElementCount { get; init; }

    public IReadOnlyList<ArrayDimensionInfo> ArrayDimensions { get; init; } = [];
}
