using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Data;

/// <summary>每连接单张默认趋势图（<c>trend_views</c> 一行 + <c>trend_view_items</c>）。</summary>
public interface ITrendViewRepository
{
    Task<TrendViewEntity> GetOrCreateDefaultViewAsync(int plcConnectionId);

    Task<TrendViewEntity?> GetViewAsync(int trendViewId);

    Task UpdateViewSettingsAsync(TrendViewEntity view);

    Task<int> SetAllSampleIntervalMsAsync(int sampleIntervalMs);

    Task<IReadOnlyList<TrendViewItemEntity>> GetViewItemsAsync(int trendViewId);

    Task AddItemToViewAsync(int trendViewId, int itemId, string? color = null);

    Task RemoveItemFromViewAsync(int trendViewId, int itemId);

    Task<bool> ViewContainsItemAsync(int trendViewId, int itemId);
}
