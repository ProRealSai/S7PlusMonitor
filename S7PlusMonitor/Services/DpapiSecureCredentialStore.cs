using System.Security.Cryptography;
using System.Text;
using S7PlusMonitor.Core.Data;

namespace S7PlusMonitor.Services;

/// <summary>使用 DPAPI 将密码加密后存于 LocalAppData，对齐 MAUI SecureStorage 语义。</summary>
public sealed class DpapiSecureCredentialStore : ISecureCredentialStore
{
    private readonly string _directory;

    public DpapiSecureCredentialStore(string directory)
    {
        _directory = directory;
        System.IO.Directory.CreateDirectory(_directory);
    }

    public Task SetAsync(string key, string value)
    {
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value ?? string.Empty),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(GetPath(key), bytes);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key)
    {
        var path = GetPath(key);
        if (!System.IO.File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                System.IO.File.ReadAllBytes(path),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        catch (CryptographicException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public void Remove(string key)
    {
        var path = GetPath(key);
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }

    private string GetPath(string key)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return System.IO.Path.Combine(_directory, safe + ".bin");
    }
}
