namespace S7PlusMonitor.Core.Helpers;

/// <summary>
/// Shell 页面可用区域度量（基于默认窗口 1280×800、最小 900×600）。
/// 计算式见各成员注释。
/// </summary>
public static class UiLayoutMetrics
{
    /// <summary>页面外边距（Padding），上下合计 24。</summary>
    public const double PagePadding = 12;

    /// <summary>主内容与状态栏间距。</summary>
    public const double PageRowSpacing = 8;

    /// <summary>Shell TabBar 高度（Windows 桌面）。</summary>
    public const double TabBarHeight = 48;

    /// <summary>CompactConnectionBar：Padding 8,4 + 按钮 MinHeight 36 ≈ 44。</summary>
    public const double TitleViewHeight = 44;

    /// <summary>StatusBar：MinHeight 36 + Padding 12,8 ≈ 52。</summary>
    public const double StatusBarHeight = 52;

    /// <summary>卡片内边距（紧凑模式）。</summary>
    public const double CompactCardPadding = 12;

    /// <summary>卡片区块间距（紧凑模式）。</summary>
    public const double CompactSectionSpacing = 8;

    /// <summary>表单行高（Entry/Picker MinHeight）。</summary>
    public const double FormControlHeight = 44;

    /// <summary>紧凑按钮行高。</summary>
    public const double CompactButtonHeight = 36;

    /// <summary>表单行间距。</summary>
    public const double FormRowSpacing = 8;

    /// <summary>双栏布局最小页面宽度（900 窗口 − 2×12 边距 − 8 列间距 ≈ 868，取 880）。</summary>
    public const double TwoColumnMinWidth = 880;

    /// <summary>浏览页变量属性区占内容高度比例（28%）。</summary>
    public const double BrowsePropertyPanelRatio = 0.28;

    /// <summary>浏览页属性区最小高度（表头 + 2 行属性）。</summary>
    public const double BrowsePropertyPanelMinHeight = 140;

    /// <summary>浏览页属性区最大高度。</summary>
    public const double BrowsePropertyPanelMaxHeight = 220;

    /// <summary>浏览页工具栏行（标题 + 按钮）高度。</summary>
    public const double BrowseToolbarHeight = 44;

    /// <summary>SearchBar 高度。</summary>
    public const double SearchBarHeight = 44;

    /// <summary>表头行高度。</summary>
    public const double TableHeaderHeight = 32;

    /// <summary>监视页单块面板标题行高度。</summary>
    public const double PanelHeaderHeight = 36;

    /// <summary>趋势图底栏（状态 + 按钮）高度。</summary>
    public const double TrendToolbarHeight = 42;

    /// <summary>
    /// Shell Tab 内容区可用高度。
    /// H_content = H_window − TabBar − TitleView − 2×PagePadding − StatusBar − PageRowSpacing
    /// 800 窗口 → 800 − 48 − 44 − 24 − 52 − 8 = 624
    /// 600 窗口 → 600 − 48 − 44 − 24 − 52 − 8 = 424
    /// </summary>
    public static double ComputeShellContentHeight(double windowHeight) =>
        windowHeight - TabBarHeight - TitleViewHeight - PagePadding * 2 - StatusBarHeight - PageRowSpacing;

    /// <summary>
    /// 浏览页变量属性区高度：clamp(H_content × 28%, 140, 220)。
    /// 800 窗口 → clamp(624×0.28, 140, 220) = 175
    /// </summary>
    public static double ComputeBrowsePropertyPanelHeight(double contentHeight) =>
        Math.Clamp(contentHeight * BrowsePropertyPanelRatio, BrowsePropertyPanelMinHeight, BrowsePropertyPanelMaxHeight);

    /// <summary>表单控件实际行高（Entry 44，Picker 36）。</summary>
    public const double FormPickerHeight = 36;

    /// <summary>带标签的表单行（标签 14 + 控件 + 间距 4）。</summary>
    public const double FormLabeledRowHeight = 54;

    /// <summary>连接页估算高度（双栏，取较高栏 ≈352 &lt; 624）。</summary>
    public const double ConnectionPageEstimatedHeight = 360;

    /// <summary>双栏平铺所需最小内容高度。</summary>
    public static double ConnectionTwoColumnMinHeight => ConnectionPageEstimatedHeight;

    /// <summary>宽 ≥880 时使用双栏布局。</summary>
    public static bool ShouldUseConnectionTwoColumn(double pageWidth, double _) =>
        pageWidth >= TwoColumnMinWidth;

    /// <summary>Windows 窗口标题栏高度。</summary>
    public const double WindowTitleBarHeight = 32;

    /// <summary>趋势设置弹窗宽度。</summary>
    public const double TrendSettingsPopupWidth = 1170;

    /// <summary>趋势设置弹窗高度。</summary>
    public const double TrendSettingsPopupHeight = 790;

    /// <summary>趋势设置弹窗最小宽度。</summary>
    public const double TrendSettingsPopupMinWidth = 1170;

    /// <summary>趋势设置弹窗最小高度。</summary>
    public const double TrendSettingsPopupMinHeight = 790;

    /// <summary>历史 X 轴动态区固定高度（滚动/绝对时间切换时不跳动）。</summary>
    public const double TrendSettingsXAxisSlotHeight = 200;

    /// <summary>兼容旧引用。</summary>
    public const double TrendSettingsPopupHeightRolling = TrendSettingsPopupHeight;

    /// <summary>兼容旧引用。</summary>
    public const double TrendSettingsPopupHeightAbsolute = TrendSettingsPopupHeight;

    /// <summary>关闭选择弹窗内容估算高度 ≈ 280。</summary>
    public const double CloseChoiceContentHeight = 280;
}
