using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

/// <summary>托盘状态文案格式化。</summary>
public static class TrayStatusFormatter
{
    public static string BuildTooltip(AppSessionViewModel session)
    {
        if (!session.IsConnected)
        {
            return "S7Plus 监视器\n○ 未连接\n双击打开主窗口";
        }

        var profileName = string.IsNullOrWhiteSpace(session.ActiveConnectionProfileName)
            ? session.ConnectionProfileDisplayName
            : session.ActiveConnectionProfileName;

        var lines = new List<string>
        {
            $"S7Plus 监视器 · {profileName}",
            $"● 已连接 {session.Host}:{session.Port}",
            BuildMonitoringLine(session),
            BuildPollingLine(session)
        };

        if (session.LastPollingUpdateLocalTime is { } updatedAt)
        {
            lines.Add($"最后更新：{updatedAt:HH:mm:ss}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildMonitoringLine(AppSessionViewModel session)
    {
        var count = session.MonitorItemCount;
        if (!session.IsMonitoringActive)
        {
            return count > 0
                ? $"监视：未启动 · 变量 {count} 个"
                : "监视：未启动";
        }

        return count > 0
            ? $"监视：运行中 · 变量 {count} 个"
            : "监视：运行中";
    }

    private static string BuildPollingLine(AppSessionViewModel session)
    {
        var summary = PollingStatusFormatter.CompactForTrayTooltip(session.PollingStatusText);
        return $"轮询：{summary}";
    }
}
