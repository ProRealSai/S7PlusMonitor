namespace S7PlusMonitor.Core.Models;

/// <summary>单次 ReadVariablesAsync 的单符号结果；Quality=Bad 时含 ErrorMessage。</summary>

/// <summary>变量读取质量。</summary>
public enum VariableReadQuality
{
    Good,
    Bad
}

public sealed class VariableReadResult
{
    public required string Symbol { get; init; }

    public object? Value { get; init; }

    public string? DisplayValue { get; init; }

    public VariableReadQuality Quality { get; init; }

    public string? ErrorMessage { get; init; }
}
