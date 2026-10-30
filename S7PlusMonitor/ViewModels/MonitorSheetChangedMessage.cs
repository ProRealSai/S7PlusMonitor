using CommunityToolkit.Mvvm.Messaging.Messages;

namespace S7PlusMonitor.ViewModels;

/// <summary>监视表内容变更消息（A2 解耦）：新增/删除监视行后由 <see cref="AppSessionViewModel.NotifyMonitorSheetChanged"/> 广播。</summary>
public sealed class MonitorSheetChangedMessage : ValueChangedMessage<int?>
{
    public MonitorSheetChangedMessage(int? connectedConnectionId) : base(connectedConnectionId)
    {
    }

    /// <summary>当前锁定的连接档案 ID（未连接为 null）；载荷经 <see cref="ValueChangedMessage{T}.Value"/> 亦可读。</summary>
    public int? ConnectedConnectionId => Value;
}
