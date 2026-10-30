using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Data;

/// <summary>趋势历史采样（<c>trend_samples</c>）读写与存储运维。</summary>
public interface ITrendSampleRepository
{
    Task InsertSamplesAsync(IReadOnlyList<TrendSampleEntity> samples);

    Task<IReadOnlyList<TrendSampleEntity>> GetSamplesAsync(int itemId, DateTimeOffset from, DateTimeOffset to);

    Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to);

    /// <summary>查询 symbol 在 (afterTimestampUtc, to] 区间内的新采样（增量刷新）。</summary>
    Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolAfterAsync(
        int plcConnectionId,
        string symbol,
        string afterTimestampUtc,
        DateTimeOffset to);

    /// <summary>按时间升序分页读取 [from, to] 内采样；<paramref name="afterTimestampUtc"/> 为空时从窗口起点开始。</summary>
    Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolPageAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        string? afterTimestampUtc,
        int limit);

    /// <summary>复合游标分页：避免同毫秒多采样丢点（timestamp 相同时按 id 递增）。</summary>
    Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolPageAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        string? afterTimestampUtc,
        int? afterId,
        int limit);

    Task<DatabaseFileStats> GetDatabaseFileStatsAsync(int plcConnectionId);

    Task<int> ClearTrendSamplesByConnectionAsync(int plcConnectionId);

    Task<int> DeleteSamplesBySymbolAsync(int plcConnectionId, string symbol);
}
