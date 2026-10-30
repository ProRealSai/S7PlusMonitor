using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary><see cref="Data.Entities.PlcConnectionEntity"/> ↔ <see cref="PlcConnectionSettings"/> 互转；SecurityMode 字符串↔枚举。</summary>
public static class PlcConnectionMapper
{
    public static PlcConnectionSettings ToSettings(
        Data.Entities.PlcConnectionEntity entity,
        string? password) =>
        new()
        {
            Host = entity.Host,
            Port = entity.Port,
            TimeoutSeconds = entity.TimeoutSeconds,
            Username = entity.Username,
            Password = password,
            SecurityMode = ParseSecurityMode(entity.SecurityMode)
        };

    public static PlcSecurityMode ParseSecurityMode(string? value) =>
        value switch
        {
            "Tls" => PlcSecurityMode.Tls,
            "LegacyChallenge" => PlcSecurityMode.LegacyChallenge,
            _ => PlcSecurityMode.Auto
        };

    public static string ToStorageValue(PlcSecurityMode mode) =>
        mode switch
        {
            PlcSecurityMode.Tls => "Tls",
            PlcSecurityMode.LegacyChallenge => "LegacyChallenge",
            _ => "Auto"
        };
}
