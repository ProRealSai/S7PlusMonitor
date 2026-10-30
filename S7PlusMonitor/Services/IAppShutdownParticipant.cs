namespace S7PlusMonitor.Services;

/// <summary>应用退出前解除事件订阅并停止后台工作，避免销毁阶段跨模块回调。</summary>
public interface IAppShutdownParticipant
{
    void PrepareForShutdown();
}
