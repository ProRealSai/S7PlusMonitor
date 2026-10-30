namespace S7PlusMonitor.Core.Data;

/// <summary>安全凭据存储（MAUI SecureStorage 抽象）。</summary>

public interface ISecureCredentialStore
{
    Task SetAsync(string key, string value);

    Task<string?> GetAsync(string key);

    void Remove(string key);
}
