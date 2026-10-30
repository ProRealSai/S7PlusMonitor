namespace S7PlusMonitor.Core.Models;

/// <summary>X 轴时间范围模式（持久化于 <c>trend_views.x_range_mode</c>）。</summary>
public enum TrendXRangeMode
{
    /// <summary>滚动时长：显示「当前 − N 秒」～「现在」。</summary>
    Rolling = 0,

    /// <summary>绝对时间：固定起点与终点（终点可跟随当前）。</summary>
    Absolute = 1
}
