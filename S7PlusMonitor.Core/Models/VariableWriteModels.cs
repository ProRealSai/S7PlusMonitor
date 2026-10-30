namespace S7PlusMonitor.Core.Models;

/// <summary>PLC 写入请求/结果模型（Symbol/Value/Success/ErrorMessage）。</summary>

public sealed class VariableWriteRequest
{
    public required string Symbol { get; init; }

    public required object Value { get; init; }
}

public sealed class VariableWriteResult
{
    public required string Symbol { get; init; }

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }
}
