namespace S7PlusMonitor.Core.Models;

/// <summary>
/// Auto 安全模式下 TLS → Legacy 的回退实现。
/// </summary>
public enum AutoSecurityFallbackMode
{
    /// <summary>驱动 <see cref="S7CommPlusDriver.S7CommPlusSecurityMode.Auto"/> 单次 Connect 内回退。</summary>
    Driver,

    /// <summary>应用层两次 ConnectAsync（Tls 失败后 LegacyChallenge）。</summary>
    Application
}
