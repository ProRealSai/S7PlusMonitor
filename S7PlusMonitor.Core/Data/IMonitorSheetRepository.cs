using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Data;

/// <summary>监视表仓储：单 sheet + 多 item 的 CRUD 与排序。</summary>

public interface IMonitorSheetRepository
{
    Task<MonitorSheetEntity?> GetSheetByConnectionAsync(int plcConnectionId);

    Task<MonitorSheetEntity> GetOrCreateSheetAsync(int plcConnectionId);

    Task<IReadOnlyList<MonitorSheetItemEntity>> GetItemsAsync(int sheetId);

    Task<int> AddItemAsync(int sheetId, string symbol, string? dataType);

    Task RemoveItemAsync(int itemId);

    Task UpdateItemDisplayFormatAsync(int itemId, string displayFormat);

    Task ReorderItemsAsync(int sheetId, IReadOnlyList<int> itemIds);

    Task<int> CountMonitorItemsByConnectionAsync(int plcConnectionId);
}
