namespace S7PlusMonitor.Core.Models;

/// <summary>PLC 安全模式（Auto/Tls/LegacyChallenge）。</summary>
public enum PlcSecurityMode
{
    Auto,
    Tls,
    LegacyChallenge
}

/// <summary>PLC 连接参数（由 <see cref="Data.Entities.PlcConnectionEntity"/> + SecureStorage 密码组装）。</summary>
public sealed class PlcConnectionSettings
{
    public required string Host { get; init; }

    public int Port { get; init; } = 102;

    public int TimeoutSeconds { get; init; } = 10;

    public int BrowseTimeoutMinutes { get; init; } = 2;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public PlcSecurityMode SecurityMode { get; init; } = PlcSecurityMode.Auto;

    /// <summary>仅 <see cref="SecurityMode.Auto"/> 时生效。</summary>
    public AutoSecurityFallbackMode AutoFallbackMode { get; init; } = AutoSecurityFallbackMode.Driver;
}
