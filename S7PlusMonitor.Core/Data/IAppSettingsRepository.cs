namespace S7PlusMonitor.Core.Data;

/// <summary>键值设置仓储（app_settings）。</summary>

public interface IAppSettingsRepository
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
}
