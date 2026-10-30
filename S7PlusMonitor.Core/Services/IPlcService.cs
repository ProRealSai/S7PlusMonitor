using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Services;

public interface IPlcService : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(PlcConnectionSettings settings, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>应用退出时断开 PLC；吞掉驱动/TLS 清理阶段的取消与异常。</summary>
    Task DisconnectForShutdownAsync();

    Task<IReadOnlyList<VariableInfo>> BrowseAllVariablesAsync(
        IProgress<PlcBrowseProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VariableReadResult>> ReadVariablesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default);

    Task<VariableWriteResult> WriteVariableAsync(
        VariableWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<VariableWriteResult> ToggleBoolAsync(
        string symbol,
        bool currentValue,
        CancellationToken cancellationToken = default);
}
