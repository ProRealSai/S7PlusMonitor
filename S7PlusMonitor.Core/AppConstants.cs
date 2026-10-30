namespace S7PlusMonitor.Core;

public static class AppConstants
{
    public const string DatabaseFileName = "s7plus_monitor.db";
    /// <summary>
    /// SecureStorage 密码键前缀（E6 防退化）：<b>永不可改</b>。
    /// MAUI SecureStorage 无枚举 API，改前缀将导致已存密码失联且无法自动发现旧键；
    /// 若确需变更，必须先把旧前缀加入 <c>SqliteAppDatabase.LegacyPasswordKeyPrefixes</c> 触发启动迁移。
    /// </summary>
    public const string SecureStoragePasswordKeyPrefix = "plc_conn_pwd_";
    public const string PollIntervalMsKey = "poll_interval_ms";
    public const string AutoSecurityFallbackKey = "auto_security_fallback";
    public const string AutoSecurityFallbackDriver = "driver";
    public const string AutoSecurityFallbackApplication = "application";
    public const int DefaultPollIntervalMs = 500;
    /// <summary>允许配置的最小监视周期（ms）。0 表示“连续轮询”（读完立即下一轮）。</summary>
    public const int MinPollIntervalMs = 0;
    public const int MaxPollIntervalMs = 60_000;
    /// <summary>引擎轮询间隔统一下限（ms）：配置 0（连续轮询）或低于此值时按此执行；
    /// 单批读取 800 符号约需数 ms，10ms 足以正确采样 10Hz 级快速信号（半周期 50ms 防混叠）。</summary>
    public const int EffectiveMinPollIntervalMs = 10;
    public const int DefaultLiveRollingWindowSeconds = 300;
    public const int DefaultHistoryRollingWindowSeconds = 300;
    /// <summary>SQLite 历史采样间隔默认值（ms）。</summary>
    public const int DefaultSampleIntervalMs = 1000;
    public const int MinSampleIntervalMs = 10;
    public const int MaxSampleIntervalMs = 3_600_000;
    public const string CloseActionKey = "close_action";
    public const string CloseActionAsk = "Ask";
    public const string CloseActionTray = "Tray";
    public const string CloseActionExit = "Exit";

    public const string TrendPanelExpandedKey = "trend_panel_expanded";
    public const int PollingStatusUiIntervalMs = 500;

    public const string DefaultTrendViewName = "趋势图";

    /// <summary>历史曲线从 SQLite 分页读入时每批条数（暂停后逐批追加到图表）。</summary>
    public const int HistoryChartLoadChunkSize = 2_000;

    /// <summary>趋势图最大入趋势数量，防止 50k 全量入趋势导致 GB 级内存。</summary>
    public const int MaxTrendItems = 500;

    /// <summary>实时缓冲每趋势项最大保留样本数（滚动窗口裁剪后的硬上限）。</summary>
    public const int MaxLiveSamplesPerItem = 600;
}
