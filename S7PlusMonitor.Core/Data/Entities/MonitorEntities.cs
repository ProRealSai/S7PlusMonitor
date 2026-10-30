using SQLite;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Data.Entities;

/// <summary>监视表/趋势视图/采样实体（SQLite 表映射）。</summary>

[Table("monitor_sheets")]
public sealed class MonitorSheetEntity
{
    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Indexed(Name = "idx_monitor_sheets_connection", Unique = true), Column("plc_connection_id")]
    public int PlcConnectionId { get; set; }

    [NotNull, Column("name")]
    public string Name { get; set; } = "监视表";

    [NotNull, Column("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [NotNull, Column("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

[Table("monitor_sheet_items")]
public sealed class MonitorSheetItemEntity
{
    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Column("sheet_id")]
    public int SheetId { get; set; }

    [NotNull, Column("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [Column("data_type")]
    public string? DataType { get; set; }

    [NotNull, Column("display_format")]
    public string DisplayFormat { get; set; } = "Hex";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [NotNull, Column("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}

[Table("trend_views")]
public sealed class TrendViewEntity
{
    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Column("plc_connection_id")]
    public int PlcConnectionId { get; set; }

    [NotNull, Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("live_rolling_window_seconds")]
    public int LiveRollingWindowSeconds { get; set; } = AppConstants.DefaultLiveRollingWindowSeconds;

    [Column("history_rolling_window_seconds")]
    public int HistoryRollingWindowSeconds { get; set; } = AppConstants.DefaultHistoryRollingWindowSeconds;

    [Column("sample_interval_ms")]
    public int SampleIntervalMs { get; set; } = AppConstants.DefaultSampleIntervalMs;

    [Column("x_range_mode")]
    public TrendXRangeMode XRangeMode { get; set; } = TrendXRangeMode.Rolling;

    [Column("x_range_start_utc")]
    public string? XRangeStartUtc { get; set; }

    [Column("x_range_end_utc")]
    public string? XRangeEndUtc { get; set; }

    [Column("x_range_end_follows_now")]
    public bool XRangeEndFollowsNow { get; set; } = true;

    [Column("live_y_auto_scale")]
    public bool LiveYAutoScale { get; set; } = true;

    [Column("live_y_min")]
    public double? LiveYMin { get; set; }

    [Column("live_y_max")]
    public double? LiveYMax { get; set; }

    [Column("history_y_auto_scale")]
    public bool HistoryYAutoScale { get; set; } = true;

    [Column("history_y_min")]
    public double? HistoryYMin { get; set; }

    [Column("history_y_max")]
    public double? HistoryYMax { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [NotNull, Column("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [NotNull, Column("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

[Table("trend_view_items")]
public sealed class TrendViewItemEntity
{
    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Column("trend_view_id")]
    public int TrendViewId { get; set; }

    [Column("item_id")]
    public int ItemId { get; set; }

    [Column("display_color")]
    public string? DisplayColor { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }
}

[Table("trend_samples")]
public sealed class TrendSampleEntity
{
    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Column("plc_connection_id")]
    public int PlcConnectionId { get; set; }

    [NotNull, Column("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [Column("item_id")]
    public int? ItemId { get; set; }

    [NotNull, Column("timestamp_utc")]
    public string TimestampUtc { get; set; } = string.Empty;

    [Column("numeric_value")]
    public double? NumericValue { get; set; }

    [Column("bool_value")]
    public bool? BoolValue { get; set; }

    [Column("quality")]
    public int Quality { get; set; }
}
