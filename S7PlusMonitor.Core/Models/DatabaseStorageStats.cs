namespace S7PlusMonitor.Core.Models;

/// <summary>数据库存储统计（文件大小 + 本连接采样数/估算字节）。</summary>

public sealed class DatabaseFileStats
{
    public required long TotalFileBytes { get; init; }

    public required ConnectionSampleStorageStats ConnectionSamples { get; init; }
}

public sealed class ConnectionSampleStorageStats
{
    public required int SampleCount { get; init; }

    public required long EstimatedBytes { get; init; }
}
