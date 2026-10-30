using S7CommPlusDriver;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>SecurityMode 字符串↔驱动 S7CommPlusSecurityMode 映射。</summary>

internal static class PlcSecurityModeMapper
{
    public static S7CommPlusSecurityMode ToDriverMode(PlcSecurityMode mode) =>
        mode switch
        {
            PlcSecurityMode.Tls => S7CommPlusSecurityMode.Tls,
            PlcSecurityMode.LegacyChallenge => S7CommPlusSecurityMode.LegacyChallenge,
            _ => S7CommPlusSecurityMode.Auto
        };
}
