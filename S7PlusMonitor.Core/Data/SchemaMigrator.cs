using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data.Entities;
using SQLite;

namespace S7PlusMonitor.Core.Data;

/// <summary>
/// SQLite 架构迁移器（从 <see cref="SqliteAppDatabase"/> 拆分）：
/// 负责 plc_connections/app_settings 建表与 monitor/trend 域 v0→10 全量迁移。
/// 所有方法为纯函数式静态，连接由调用方（SqliteAppDatabase）持有并保证锁内调用。
/// </summary>
internal static class SchemaMigrator
{
    private const int CurrentSchemaVersion = 10;

    /// <summary>建基础表并执行 monitor/trend 域 schema 迁移至 <see cref="CurrentSchemaVersion"/>。</summary>
    public static async Task EnsureSchemaAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await MigrateLegacyPlcConnectionsColumnsAsync(connection).ConfigureAwait(false);
        await connection.CreateTableAsync<PlcConnectionEntity>().ConfigureAwait(false);
        await connection.CreateTableAsync<AppSettingEntity>().ConfigureAwait(false);
        await EnsureMonitorSchemaAsync(connection).ConfigureAwait(false);
    }

    private static async Task EnsureMonitorSchemaAsync(SQLiteAsyncConnection connection)
    {
        var versionRow = await connection.FindAsync<AppSettingEntity>("schema_version").ConfigureAwait(false);
        var version = int.TryParse(versionRow?.Value, out var parsed) ? parsed : 0;

        if (version < 4)
        {
            if (version == 0)
            {
                await CreateSchemaV4Async(connection).ConfigureAwait(false);
            }
            else
            {
                await MigrateToSchemaV4Async(connection).ConfigureAwait(false);
            }
        }
        else
        {
            await CreateSchemaV4Async(connection).ConfigureAwait(false);
        }

        if (version < 5)
        {
            await MigrateToSchemaV5Async(connection).ConfigureAwait(false);
        }

        if (version < 6)
        {
            await MigrateToSchemaV6Async(connection).ConfigureAwait(false);
        }

        if (version < 7)
        {
            await MigrateToSchemaV7Async(connection).ConfigureAwait(false);
        }

        if (version < 8)
        {
            await MigrateToSchemaV8Async(connection).ConfigureAwait(false);
        }

        if (version < 9)
        {
            await MigrateToSchemaV9Async(connection).ConfigureAwait(false);
        }

        if (version < 10)
        {
            await MigrateToSchemaV10Async(connection).ConfigureAwait(false);
        }

        if (version != CurrentSchemaVersion)
        {
            await connection.InsertOrReplaceAsync(new AppSettingEntity
            {
                Key = "schema_version",
                Value = CurrentSchemaVersion.ToString()
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// sqlite-net 曾以属性名建父表（"Id"/"TimeoutSeconds"），子表 FK 引用 plc_connections(id)。
    /// 在 CreateTable 之前把旧列迁成 snake_case，避免 ADD COLUMN 并存两套主键。
    /// </summary>
    private static async Task MigrateLegacyPlcConnectionsColumnsAsync(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "plc_connections").ConfigureAwait(false))
        {
            return;
        }

        if (!await HasExactColumnAsync(connection, "plc_connections", "Id").ConfigureAwait(false)
            && !await HasExactColumnAsync(connection, "plc_connections", "TimeoutSeconds").ConfigureAwait(false))
        {
            return;
        }

        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;").ConfigureAwait(false);
        try
        {
            await connection.ExecuteAsync("DROP TABLE IF EXISTS plc_connections_v10;").ConfigureAwait(false);
            await connection.ExecuteAsync("""
                CREATE TABLE plc_connections_v10 (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    host TEXT NOT NULL,
                    port INTEGER NOT NULL DEFAULT 102,
                    timeout_seconds INTEGER NOT NULL DEFAULT 10,
                    username TEXT,
                    security_mode TEXT NOT NULL DEFAULT 'Auto',
                    is_default INTEGER NOT NULL DEFAULT 0,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    last_used_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """).ConfigureAwait(false);

            await connection.ExecuteAsync("""
                INSERT INTO plc_connections_v10 (
                    id, name, host, port, timeout_seconds, username, security_mode,
                    is_default, sort_order, last_used_at, created_at, updated_at)
                SELECT
                    "Id",
                    "Name",
                    "Host",
                    "Port",
                    "TimeoutSeconds",
                    "Username",
                    "SecurityMode",
                    "IsDefault",
                    "SortOrder",
                    "LastUsedAt",
                    "CreatedAt",
                    "UpdatedAt"
                FROM plc_connections;
                """).ConfigureAwait(false);

            await connection.ExecuteAsync("DROP TABLE plc_connections;").ConfigureAwait(false);
            await connection.ExecuteAsync("ALTER TABLE plc_connections_v10 RENAME TO plc_connections;").ConfigureAwait(false);
        }
        finally
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);
        }
    }

    private static async Task MigrateToSchemaV10Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "plc_connections").ConfigureAwait(false))
        {
            return;
        }

        await connection.ExecuteAsync("""
            UPDATE plc_connections
            SET is_default = 0
            WHERE is_default = 1
              AND id != (
                    SELECT id
                    FROM plc_connections
                    WHERE is_default = 1
                    ORDER BY id
                    LIMIT 1
                );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS idx_plc_connections_one_default
            ON plc_connections(is_default) WHERE is_default = 1;
            """).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV4Async(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS monitor_sheets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plc_connection_id INTEGER NOT NULL UNIQUE,
                name TEXT NOT NULL DEFAULT '监视表',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(plc_connection_id) REFERENCES plc_connections(id) ON DELETE CASCADE
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS monitor_sheet_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sheet_id INTEGER NOT NULL,
                symbol TEXT NOT NULL,
                data_type TEXT,
                display_format TEXT NOT NULL DEFAULT 'Hex',
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                FOREIGN KEY(sheet_id) REFERENCES monitor_sheets(id) ON DELETE CASCADE,
                UNIQUE(sheet_id, symbol)
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS trend_views (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plc_connection_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                live_rolling_window_seconds INTEGER NOT NULL DEFAULT 300,
                history_rolling_window_seconds INTEGER NOT NULL DEFAULT 300,
                sample_interval_ms INTEGER NOT NULL DEFAULT 1000,
                x_range_mode INTEGER NOT NULL DEFAULT 0,
                x_range_start_utc TEXT,
                x_range_end_utc TEXT,
                x_range_end_follows_now INTEGER NOT NULL DEFAULT 1,
                live_y_auto_scale INTEGER NOT NULL DEFAULT 1,
                live_y_min REAL,
                live_y_max REAL,
                history_y_auto_scale INTEGER NOT NULL DEFAULT 1,
                history_y_min REAL,
                history_y_max REAL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(plc_connection_id) REFERENCES plc_connections(id) ON DELETE CASCADE,
                UNIQUE(plc_connection_id, name)
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS trend_view_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                trend_view_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                display_color TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(trend_view_id) REFERENCES trend_views(id) ON DELETE CASCADE,
                FOREIGN KEY(item_id) REFERENCES monitor_sheet_items(id) ON DELETE CASCADE,
                UNIQUE(trend_view_id, item_id)
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS trend_samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plc_connection_id INTEGER NOT NULL,
                symbol TEXT NOT NULL,
                item_id INTEGER,
                timestamp_utc TEXT NOT NULL,
                numeric_value REAL,
                bool_value INTEGER,
                quality INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(plc_connection_id) REFERENCES plc_connections(id) ON DELETE CASCADE,
                FOREIGN KEY(item_id) REFERENCES monitor_sheet_items(id) ON DELETE SET NULL
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_samples_conn_symbol_time ON trend_samples(plc_connection_id, symbol, timestamp_utc);")
            .ConfigureAwait(false);
    }

    private static async Task MigrateToSchemaV4Async(SQLiteAsyncConnection connection)
    {
        var hasLegacySheets = await TableExistsAsync(connection, "monitor_sheets").ConfigureAwait(false);
        if (!hasLegacySheets)
        {
            await CreateSchemaV4Async(connection).ConfigureAwait(false);
            return;
        }

        var legacySheets = await connection.QueryAsync<LegacyMonitorSheetRow>(
            """
            SELECT id AS Id,
                   plc_connection_id AS PlcConnectionId,
                   name AS Name,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM monitor_sheets
            ORDER BY plc_connection_id, id;
            """)
            .ConfigureAwait(false);

        var legacyItems = await connection.QueryAsync<LegacyMonitorItemRow>(
            """
            SELECT id AS Id,
                   sheet_id AS SheetId,
                   symbol AS Symbol,
                   data_type AS DataType,
                   display_format AS DisplayFormat,
                   sort_order AS SortOrder,
                   created_at AS CreatedAt
            FROM monitor_sheet_items;
            """)
            .ConfigureAwait(false);

        await connection.ExecuteAsync("DROP TABLE IF EXISTS trend_samples;").ConfigureAwait(false);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS trend_view_items;").ConfigureAwait(false);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS trend_views;").ConfigureAwait(false);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS monitor_sheet_items;").ConfigureAwait(false);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS monitor_sheets;").ConfigureAwait(false);

        await CreateSchemaV4Async(connection).ConfigureAwait(false);

        var connectionPrimarySheet = new Dictionary<int, int>();
        foreach (var legacySheet in legacySheets)
        {
            if (connectionPrimarySheet.ContainsKey(legacySheet.PlcConnectionId))
            {
                continue;
            }

            var sheet = new MonitorSheetEntity
            {
                PlcConnectionId = legacySheet.PlcConnectionId,
                Name = legacySheet.Name,
                CreatedAt = legacySheet.CreatedAt,
                UpdatedAt = legacySheet.UpdatedAt
            };
            await connection.InsertAsync(sheet).ConfigureAwait(false);
            connectionPrimarySheet[legacySheet.PlcConnectionId] = sheet.Id;
        }

        var sheetConnectionMap = legacySheets.ToDictionary(s => s.Id, s => s.PlcConnectionId);
        var mergedSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var legacyItem in legacyItems.OrderBy(item => item.Id))
        {
            if (!sheetConnectionMap.TryGetValue(legacyItem.SheetId, out var connectionId)
                || !connectionPrimarySheet.TryGetValue(connectionId, out var targetSheetId))
            {
                continue;
            }

            var symbolKey = $"{targetSheetId}:{legacyItem.Symbol}";
            if (!mergedSymbols.Add(symbolKey))
            {
                continue;
            }

            await connection.InsertAsync(new MonitorSheetItemEntity
            {
                SheetId = targetSheetId,
                Symbol = legacyItem.Symbol,
                DataType = legacyItem.DataType,
                DisplayFormat = string.IsNullOrWhiteSpace(legacyItem.DisplayFormat) ? "Hex" : legacyItem.DisplayFormat,
                SortOrder = legacyItem.SortOrder,
                CreatedAt = legacyItem.CreatedAt
            }).ConfigureAwait(false);
        }
    }

    private static async Task MigrateToSchemaV5Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "trend_views").ConfigureAwait(false))
        {
            return;
        }

        await AddColumnIfMissingAsync(connection, "trend_views", "x_range_mode", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "x_range_start_utc", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "x_range_end_utc", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "x_range_end_follows_now", "INTEGER NOT NULL DEFAULT 1").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "y_min", "REAL").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "y_max", "REAL").ConfigureAwait(false);
    }

    private static async Task MigrateToSchemaV6Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "trend_views").ConfigureAwait(false))
        {
            return;
        }

        await AddColumnIfMissingAsync(
            connection,
            "trend_views",
            "sample_interval_ms",
            $"INTEGER NOT NULL DEFAULT {AppConstants.DefaultSampleIntervalMs}").ConfigureAwait(false);
    }

    private static async Task MigrateToSchemaV7Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "trend_samples").ConfigureAwait(false))
        {
            return;
        }

        if (await ColumnExistsAsync(connection, "trend_samples", "plc_connection_id").ConfigureAwait(false))
        {
            return;
        }

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS trend_samples_v7 (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plc_connection_id INTEGER NOT NULL,
                symbol TEXT NOT NULL,
                item_id INTEGER,
                timestamp_utc TEXT NOT NULL,
                numeric_value REAL,
                bool_value INTEGER,
                quality INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(plc_connection_id) REFERENCES plc_connections(id) ON DELETE CASCADE,
                FOREIGN KEY(item_id) REFERENCES monitor_sheet_items(id) ON DELETE SET NULL
            );
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("""
            INSERT INTO trend_samples_v7 (
                id, plc_connection_id, symbol, item_id, timestamp_utc, numeric_value, bool_value, quality)
            SELECT s.id,
                   sh.plc_connection_id,
                   i.symbol,
                   s.item_id,
                   s.timestamp_utc,
                   s.numeric_value,
                   s.bool_value,
                   s.quality
            FROM trend_samples s
            INNER JOIN monitor_sheet_items i ON i.id = s.item_id
            INNER JOIN monitor_sheets sh ON sh.id = i.sheet_id;
            """).ConfigureAwait(false);

        await connection.ExecuteAsync("DROP TABLE trend_samples;").ConfigureAwait(false);
        await connection.ExecuteAsync("ALTER TABLE trend_samples_v7 RENAME TO trend_samples;").ConfigureAwait(false);
        await connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_samples_conn_symbol_time ON trend_samples(plc_connection_id, symbol, timestamp_utc);")
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_samples_item_time ON trend_samples(item_id, timestamp_utc);")
            .ConfigureAwait(false);
    }

    private static async Task MigrateToSchemaV8Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "trend_views").ConfigureAwait(false))
        {
            return;
        }

        if (await ColumnExistsAsync(connection, "trend_views", "live_rolling_window_seconds").ConfigureAwait(false))
        {
            return;
        }

        await AddColumnIfMissingAsync(
            connection,
            "trend_views",
            "live_history_window_seconds",
            $"INTEGER NOT NULL DEFAULT {AppConstants.DefaultLiveRollingWindowSeconds}").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "live_y_auto_scale", "INTEGER NOT NULL DEFAULT 1").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "live_y_min", "REAL").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "live_y_max", "REAL").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "history_y_auto_scale", "INTEGER NOT NULL DEFAULT 1").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "history_y_min", "REAL").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "trend_views", "history_y_max", "REAL").ConfigureAwait(false);

        if (!await ColumnExistsAsync(connection, "trend_views", "history_window_seconds").ConfigureAwait(false))
        {
            return;
        }

        var hasLegacyYAxis = await ColumnExistsAsync(connection, "trend_views", "y_auto_scale").ConfigureAwait(false);
        if (hasLegacyYAxis)
        {
            await connection.ExecuteAsync("""
                UPDATE trend_views
                SET live_history_window_seconds = CASE
                        WHEN history_window_seconds < 1 THEN 1
                        WHEN history_window_seconds > 3600 THEN 3600
                        ELSE history_window_seconds
                    END,
                    live_y_auto_scale = y_auto_scale,
                    live_y_min = y_min,
                    live_y_max = y_max,
                    history_y_auto_scale = y_auto_scale,
                    history_y_min = y_min,
                    history_y_max = y_max;
                """).ConfigureAwait(false);
        }
        else
        {
            await connection.ExecuteAsync("""
                UPDATE trend_views
                SET live_history_window_seconds = CASE
                        WHEN history_window_seconds < 1 THEN 1
                        WHEN history_window_seconds > 3600 THEN 3600
                        ELSE history_window_seconds
                    END;
                """).ConfigureAwait(false);
        }
    }

    private static async Task MigrateToSchemaV9Async(SQLiteAsyncConnection connection)
    {
        if (!await TableExistsAsync(connection, "trend_views").ConfigureAwait(false))
        {
            return;
        }

        await RenameColumnIfExistsAsync(
            connection,
            "trend_views",
            "history_window_seconds",
            "history_rolling_window_seconds").ConfigureAwait(false);
        await RenameColumnIfExistsAsync(
            connection,
            "trend_views",
            "live_history_window_seconds",
            "live_rolling_window_seconds").ConfigureAwait(false);
    }

    private static async Task RenameColumnIfExistsAsync(
        SQLiteAsyncConnection connection,
        string tableName,
        string oldColumnName,
        string newColumnName)
    {
        if (!await ColumnExistsAsync(connection, tableName, oldColumnName).ConfigureAwait(false))
        {
            return;
        }

        if (await ColumnExistsAsync(connection, tableName, newColumnName).ConfigureAwait(false))
        {
            return;
        }

        await connection.ExecuteAsync(
            $"ALTER TABLE {tableName} RENAME COLUMN {oldColumnName} TO {newColumnName};").ConfigureAwait(false);
    }

    private static async Task AddColumnIfMissingAsync(
        SQLiteAsyncConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        if (await ColumnExistsAsync(connection, tableName, columnName).ConfigureAwait(false))
        {
            return;
        }

        await connection.ExecuteAsync(
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};").ConfigureAwait(false);
    }

    private static async Task<bool> ColumnExistsAsync(SQLiteAsyncConnection connection, string tableName, string columnName)
    {
        var columns = await connection.QueryAsync<TableInfoRow>(
            $"PRAGMA table_info({tableName});").ConfigureAwait(false);
        return columns.Any(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> HasExactColumnAsync(SQLiteAsyncConnection connection, string tableName, string columnName)
    {
        var columns = await connection.QueryAsync<TableInfoRow>(
            $"PRAGMA table_info({tableName});").ConfigureAwait(false);
        return columns.Any(column => string.Equals(column.Name, columnName, StringComparison.Ordinal));
    }

    private static async Task<bool> TableExistsAsync(SQLiteAsyncConnection connection, string tableName)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?;",
            tableName).ConfigureAwait(false);
        return count > 0;
    }

    private sealed class LegacyMonitorSheetRow
    {
        public int Id { get; set; }

        public int PlcConnectionId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class LegacyMonitorItemRow
    {
        public int Id { get; set; }

        public int SheetId { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public string? DataType { get; set; }

        public string DisplayFormat { get; set; } = "Hex";

        public int SortOrder { get; set; }

        public string CreatedAt { get; set; } = string.Empty;
    }

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
