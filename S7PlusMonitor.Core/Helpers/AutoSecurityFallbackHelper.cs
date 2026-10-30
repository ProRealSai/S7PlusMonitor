using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>Auto 回退模式解析与序列化（driver/application）。</summary>

public static class AutoSecurityFallbackHelper
{
    public static AutoSecurityFallbackMode ParseOrDefault(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            AppConstants.AutoSecurityFallbackApplication => AutoSecurityFallbackMode.Application,
            _ => AutoSecurityFallbackMode.Driver
        };

    public static string ToStorageValue(AutoSecurityFallbackMode mode) =>
        mode == AutoSecurityFallbackMode.Application
            ? AppConstants.AutoSecurityFallbackApplication
            : AppConstants.AutoSecurityFallbackDriver;
}
