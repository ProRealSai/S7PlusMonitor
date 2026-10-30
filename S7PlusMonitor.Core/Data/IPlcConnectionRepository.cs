using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Data;

public interface IPlcConnectionRepository
{
    Task<IReadOnlyList<PlcConnectionEntity>> GetAllAsync();

    Task<PlcConnectionEntity?> GetAsync(int id);

    Task<PlcConnectionEntity?> GetByNameAsync(string name);

    /// <summary>按名称查找（忽略大小写），用于保存 upsert。</summary>
    Task<PlcConnectionEntity?> FindByNameAsync(string name);

    Task<PlcConnectionEntity?> GetDefaultAsync();

    Task<int> CreateAsync(PlcConnectionEntity entity);

    Task UpdateAsync(PlcConnectionEntity entity);

    Task DeleteAsync(int id);

    Task<int> CountMonitorItemsByConnectionAsync(int connectionId);

    Task<int> CountTrendViewsByConnectionAsync(int connectionId);

    Task SavePasswordAsync(int connectionId, string password);

    Task<string?> GetPasswordAsync(int connectionId);

    Task ClearPasswordAsync(int connectionId);

    Task UpdateLastUsedAsync(int connectionId);
}
