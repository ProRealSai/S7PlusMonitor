using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.ViewModels;

/// <summary>Auto 回退模式下拉选项（驱动内置/应用两次连接）。</summary>

public sealed class AutoFallbackModeOption
{
    public required string Label { get; init; }

    public required AutoSecurityFallbackMode Mode { get; init; }
}
