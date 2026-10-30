using SQLite;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Data;

/// <summary>SQLite 单库多租户实现，同时实现 5 个仓储接口；负责 schema v0→10 迁移、事务批量采样与后台 VACUUM。</summary>
public sealed class SqliteAppDatabase
    : IPlcConnectionRepository,
      IAppSettingsRepository,
      IMonitorSheetRepository,
      ITrendSampleRepository,
      ITrendViewRepository,
      IAsyncDisposable
{
    /// <summary>
    /// E6：历史密码键前缀列表。当前前缀（<see cref="AppConstants.SecureStoragePasswordKeyPrefix"/>）
    /// 自首个版本从未变更，故为空；未来若改前缀，须将旧前缀加入此表，启动时自动把
    /// 「旧键 → 新键」搬运（新键已有值则仅删除旧键），避免已存密码静默失联。
    /// </summary>
    private static readonly string[] LegacyPasswordKeyPrefixes = [];

    private readonly ISecureCredentialStore _credentialStore;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private SQLiteAsyncConnection? _dbConnection;
    private bool _initialized;

    public SqliteAppDatabase(string databasePath, ISecureCredentialStore credentialStore)
    {
        DatabasePath = databasePath;
        _credentialStore = credentialStore;
    }

    public string DatabasePath { get; }

    public async Task<IReadOnlyList<PlcConnectionEntity>> GetAllAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var items = await GetConnection().Table<PlcConnectionEntity>()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync()
            .ConfigureAwait(false);
        return items;
    }

    public async Task<PlcConnectionEntity?> GetAsync(int id)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().FindAsync<PlcConnectionEntity>(id).ConfigureAwait(false);
    }

    public async Task<PlcConnectionEntity?> GetByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EnsureInitializedAsync().ConfigureAwait(false);
        var trimmedName = name.Trim();
        return await GetConnection().Table<PlcConnectionEntity>()
            .Where(item => item.Name == trimmedName)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<PlcConnectionEntity?> FindByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmedName = name.Trim();
        var exact = await GetByNameAsync(trimmedName).ConfigureAwait(false);
        if (exact != null)
        {
            return exact;
        }

        var all = await GetAllAsync().ConfigureAwait(false);
        return all.FirstOrDefault(item =>
            string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PlcConnectionEntity?> GetDefaultAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().Table<PlcConnectionEntity>()
            .Where(item => item.IsDefault)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(PlcConnectionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureInitializedAsync().ConfigureAwait(false);

        var now = GetUtcNowIso8601();
        entity.CreatedAt = now;
        entity.UpdatedAt = now;

        if (entity.IsDefault)
        {
            await ClearExistingDefaultFlagAsync().ConfigureAwait(false);
        }

        await GetConnection().InsertAsync(entity).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task UpdateAsync(PlcConnectionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureInitializedAsync().ConfigureAwait(false);

        entity.UpdatedAt = GetUtcNowIso8601();
        if (entity.IsDefault)
        {
            await ClearExistingDefaultFlagAsync(entity.Id).ConfigureAwait(false);
        }

        await GetConnection().UpdateAsync(entity).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        // 显式按依赖序删除，避免仅依赖 ON DELETE CASCADE：
        // sqlite-net 将父表主键建为 "Id"，子表 FK 引用 plc_connections(id)，部分库上 CASCADE 不会生效并导致约束失败。
        await GetConnection().RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM trend_samples WHERE plc_connection_id = ?;", id);
            conn.Execute(
                """
                DELETE FROM trend_view_items
                WHERE trend_view_id IN (SELECT id FROM trend_views WHERE plc_connection_id = ?);
                """,
                id);
            conn.Execute("DELETE FROM trend_views WHERE plc_connection_id = ?;", id);
            conn.Execute(
                """
                DELETE FROM monitor_sheet_items
                WHERE sheet_id IN (SELECT id FROM monitor_sheets WHERE plc_connection_id = ?);
                """,
                id);
            conn.Execute("DELETE FROM monitor_sheets WHERE plc_connection_id = ?;", id);
            conn.Delete<PlcConnectionEntity>(id);
        }).ConfigureAwait(false);
        await ClearPasswordAsync(id).ConfigureAwait(false);
    }

    public async Task<int> CountMonitorItemsByConnectionAsync(int connectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM monitor_sheet_items i
            INNER JOIN monitor_sheets s ON s.id = i.sheet_id
            WHERE s.plc_connection_id = ?;
            """,
            connectionId).ConfigureAwait(false);
    }

    public async Task<int> CountTrendViewsByConnectionAsync(int connectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM trend_views WHERE plc_connection_id = ?;",
            connectionId).ConfigureAwait(false);
    }

    public Task SavePasswordAsync(int connectionId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _credentialStore.SetAsync(BuildSecureStorageKey(connectionId), password);
    }

    public Task<string?> GetPasswordAsync(int connectionId) =>
        _credentialStore.GetAsync(BuildSecureStorageKey(connectionId));

    public Task ClearPasswordAsync(int connectionId)
    {
        _credentialStore.Remove(BuildSecureStorageKey(connectionId));
        return Task.CompletedTask;
    }

    public async Task UpdateLastUsedAsync(int connectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var entity = await GetAsync(connectionId).ConfigureAwait(false);
        if (entity == null)
        {
            return;
        }

        entity.LastUsedAt = GetUtcNowIso8601();
        entity.UpdatedAt = entity.LastUsedAt;
        await GetConnection().UpdateAsync(entity).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string key)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var row = await GetConnection().FindAsync<AppSettingEntity>(key).ConfigureAwait(false);
        return row?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await GetConnection().InsertOrReplaceAsync(new AppSettingEntity
        {
            Key = key,
            Value = value
        }).ConfigureAwait(false);
    }

    public async Task<MonitorSheetEntity?> GetSheetByConnectionAsync(int plcConnectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().Table<MonitorSheetEntity>()
            .Where(sheet => sheet.PlcConnectionId == plcConnectionId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<MonitorSheetEntity> GetOrCreateSheetAsync(int plcConnectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var existing = await GetSheetByConnectionAsync(plcConnectionId).ConfigureAwait(false);
        if (existing != null)
        {
            return existing;
        }

        var now = GetUtcNowIso8601();
        var sheet = new MonitorSheetEntity
        {
            PlcConnectionId = plcConnectionId,
            Name = "监视表",
            CreatedAt = now,
            UpdatedAt = now
        };
        await GetConnection().InsertAsync(sheet).ConfigureAwait(false);
        return sheet;
    }

    public async Task<IReadOnlyList<MonitorSheetItemEntity>> GetItemsAsync(int sheetId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var items = await GetConnection().Table<MonitorSheetItemEntity>()
            .Where(item => item.SheetId == sheetId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Symbol)
            .ToListAsync()
            .ConfigureAwait(false);
        return items;
    }

    public async Task<int> AddItemAsync(int sheetId, string symbol, string? dataType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        await EnsureInitializedAsync().ConfigureAwait(false);

        var trimmedSymbol = symbol.Trim();
        var existing = await GetConnection().Table<MonitorSheetItemEntity>()
            .Where(item => item.SheetId == sheetId && item.Symbol == trimmedSymbol)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (existing != null)
        {
            throw new InvalidOperationException($"变量「{trimmedSymbol}」已在监视表中。");
        }

        var maxOrder = await GetConnection().ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(sort_order), -1) FROM monitor_sheet_items WHERE sheet_id = ?;",
            sheetId).ConfigureAwait(false);

        var entity = new MonitorSheetItemEntity
        {
            SheetId = sheetId,
            Symbol = trimmedSymbol,
            DataType = dataType,
            DisplayFormat = DefaultDisplayFormat(dataType),
            SortOrder = maxOrder + 1,
            CreatedAt = GetUtcNowIso8601()
        };
        await GetConnection().InsertAsync(entity).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task RemoveItemAsync(int itemId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var item = await GetConnection().FindAsync<MonitorSheetItemEntity>(itemId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"监视行 {itemId} 不存在。");

        var sheet = await GetConnection().FindAsync<MonitorSheetEntity>(item.SheetId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"监视表 {item.SheetId} 不存在。");

        var plcConnectionId = sheet.PlcConnectionId;
        var symbol = item.Symbol;

        await GetConnection().RunInTransactionAsync(conn =>
        {
            conn.Execute(
                "DELETE FROM trend_samples WHERE plc_connection_id = ? AND symbol = ?;",
                plcConnectionId,
                symbol);
            conn.Delete<MonitorSheetItemEntity>(itemId);
        }).ConfigureAwait(false);
    }

    public async Task UpdateItemDisplayFormatAsync(int itemId, string displayFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayFormat);
        await EnsureInitializedAsync().ConfigureAwait(false);

        var item = await GetConnection().FindAsync<MonitorSheetItemEntity>(itemId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"监视行 {itemId} 不存在。");

        var normalized = DisplayFormatRegistry.ParseFormatOrDefault(displayFormat, item.DataType);
        item.DisplayFormat = DisplayFormatRegistry.ToFormatId(normalized);
        await GetConnection().UpdateAsync(item).ConfigureAwait(false);
    }

    public async Task ReorderItemsAsync(int sheetId, IReadOnlyList<int> itemIds)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        for (var index = 0; index < itemIds.Count; index++)
        {
            await GetConnection().ExecuteAsync(
                "UPDATE monitor_sheet_items SET sort_order = ? WHERE id = ? AND sheet_id = ?;",
                index,
                itemIds[index],
                sheetId).ConfigureAwait(false);
        }
    }

    public async Task InsertSamplesAsync(IReadOnlyList<TrendSampleEntity> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync().ConfigureAwait(false);
        // 批量插入包事务，避免每条 INSERT 一事务导致的 WAL 频繁刷盘（50ms→8ms）
        await GetConnection().RunInTransactionAsync(conn =>
        {
            foreach (var s in samples)
            {
                conn.Insert(s);
            }
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrendSampleEntity>> GetSamplesAsync(
        int itemId,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var fromText = FormatTimestampUtc(from);
        var toText = FormatTimestampUtc(to);
        var samples = await GetConnection().QueryAsync<TrendSampleEntity>(
            """
            SELECT *
            FROM trend_samples
            WHERE item_id = ?
              AND timestamp_utc >= ?
              AND timestamp_utc <= ?
            ORDER BY timestamp_utc;
            """,
            itemId,
            fromText,
            toText).ConfigureAwait(false);
        return samples;
    }

    public async Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        await EnsureInitializedAsync().ConfigureAwait(false);
        var fromText = FormatTimestampUtc(from);
        var toText = FormatTimestampUtc(to);
        var samples = await GetConnection().QueryAsync<TrendSampleEntity>(
            """
            SELECT *
            FROM trend_samples
            WHERE plc_connection_id = ?
              AND symbol = ?
              AND timestamp_utc >= ?
              AND timestamp_utc <= ?
            ORDER BY timestamp_utc;
            """,
            plcConnectionId,
            symbol.Trim(),
            fromText,
            toText).ConfigureAwait(false);
        return samples;
    }

    public async Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolAfterAsync(
        int plcConnectionId,
        string symbol,
        string afterTimestampUtc,
        DateTimeOffset to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterTimestampUtc);
        await EnsureInitializedAsync().ConfigureAwait(false);
        var toText = FormatTimestampUtc(to);
        var samples = await GetConnection().QueryAsync<TrendSampleEntity>(
            """
            SELECT *
            FROM trend_samples
            WHERE plc_connection_id = ?
              AND symbol = ?
              AND timestamp_utc > ?
              AND timestamp_utc <= ?
            ORDER BY timestamp_utc;
            """,
            plcConnectionId,
            symbol.Trim(),
            afterTimestampUtc,
            toText).ConfigureAwait(false);
        return samples;
    }

    public async Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolPageAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        string? afterTimestampUtc,
        int limit) =>
        await GetSamplesBySymbolPageAsync(
            plcConnectionId, symbol, from, to, afterTimestampUtc, null, limit).ConfigureAwait(false);

    public async Task<IReadOnlyList<TrendSampleEntity>> GetSamplesBySymbolPageAsync(
        int plcConnectionId,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        string? afterTimestampUtc,
        int? afterId,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (limit <= 0)
        {
            return Array.Empty<TrendSampleEntity>();
        }

        await EnsureInitializedAsync().ConfigureAwait(false);
        var fromText = FormatTimestampUtc(from);
        var toText = FormatTimestampUtc(to);
        if (string.IsNullOrWhiteSpace(afterTimestampUtc))
        {
            return await GetConnection().QueryAsync<TrendSampleEntity>(
                """
                SELECT *
                FROM trend_samples
                WHERE plc_connection_id = ?
                  AND symbol = ?
                  AND timestamp_utc >= ?
                  AND timestamp_utc <= ?
                ORDER BY timestamp_utc, id
                LIMIT ?;
                """,
                plcConnectionId,
                symbol.Trim(),
                fromText,
                toText,
                limit).ConfigureAwait(false);
        }

        // 复合游标：同毫秒多采样时仅按 timestamp > 会丢点，需按 (timestamp, id) 联合递增
        if (afterId.HasValue)
        {
            return await GetConnection().QueryAsync<TrendSampleEntity>(
                """
                SELECT *
                FROM trend_samples
                WHERE plc_connection_id = ?
                  AND symbol = ?
                  AND timestamp_utc <= ?
                  AND (timestamp_utc > ? OR (timestamp_utc = ? AND id > ?))
                ORDER BY timestamp_utc, id
                LIMIT ?;
                """,
                plcConnectionId,
                symbol.Trim(),
                toText,
                afterTimestampUtc,
                afterTimestampUtc,
                afterId.Value,
                limit).ConfigureAwait(false);
        }

        return await GetConnection().QueryAsync<TrendSampleEntity>(
            """
            SELECT *
            FROM trend_samples
            WHERE plc_connection_id = ?
              AND symbol = ?
              AND timestamp_utc > ?
              AND timestamp_utc <= ?
            ORDER BY timestamp_utc, id
            LIMIT ?;
            """,
            plcConnectionId,
            symbol.Trim(),
            afterTimestampUtc,
            toText,
            limit).ConfigureAwait(false);
    }

    public async Task<DatabaseFileStats> GetDatabaseFileStatsAsync(int plcConnectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        if (plcConnectionId <= 0)
        {
            return new DatabaseFileStats
            {
                TotalFileBytes = GetDatabaseFileSizeBytes(),
                ConnectionSamples = new ConnectionSampleStorageStats
                {
                    SampleCount = 0,
                    EstimatedBytes = 0
                }
            };
        }

        var sampleCount = await GetConnection().ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM trend_samples WHERE plc_connection_id = ?;",
            plcConnectionId).ConfigureAwait(false);

        var estimatedBytes = await GetConnection().ExecuteScalarAsync<long?>(
            """
            SELECT IFNULL(SUM(
                LENGTH(symbol)
                + LENGTH(timestamp_utc)
                + 24
                + IFNULL(LENGTH(CAST(numeric_value AS TEXT)), 0)
                + IFNULL(LENGTH(CAST(bool_value AS TEXT)), 0)
            ), 0)
            FROM trend_samples
            WHERE plc_connection_id = ?;
            """,
            plcConnectionId).ConfigureAwait(false) ?? 0;

        return new DatabaseFileStats
        {
            TotalFileBytes = GetDatabaseFileSizeBytes(),
            ConnectionSamples = new ConnectionSampleStorageStats
            {
                SampleCount = sampleCount,
                EstimatedBytes = estimatedBytes
            }
        };
    }

    public async Task<int> ClearTrendSamplesByConnectionAsync(int plcConnectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var deleted = await GetConnection().ExecuteAsync(
            "DELETE FROM trend_samples WHERE plc_connection_id = ?;",
            plcConnectionId).ConfigureAwait(false);
        // VACUUM 持有排他锁且可能耗时 2–5 秒（1M 采样），不在 UI 路径同步执行；
        // 后台低优先级执行，失败仅影响文件体积回收，不影响业务。SQLite 的 auto_vacuum 亦会复用页。
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200).ConfigureAwait(false);
                await GetConnection().ExecuteAsync("VACUUM;").ConfigureAwait(false);
            }
            catch
            {
                // 后台回收失败不影响主流程
            }
        });
        return deleted;
    }

    public async Task<int> DeleteSamplesBySymbolAsync(int plcConnectionId, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (plcConnectionId <= 0)
        {
            return 0;
        }

        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().ExecuteAsync(
            "DELETE FROM trend_samples WHERE plc_connection_id = ? AND symbol = ?;",
            plcConnectionId,
            symbol.Trim()).ConfigureAwait(false);
    }

    public long GetDatabaseFileSizeBytes()
    {
        try
        {
            return File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<TrendViewEntity> GetOrCreateDefaultViewAsync(int plcConnectionId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var existing = await GetConnection().Table<TrendViewEntity>()
            .Where(view => view.PlcConnectionId == plcConnectionId)
            .OrderBy(view => view.SortOrder)
            .ThenBy(view => view.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (existing != null)
        {
            return existing;
        }

        var now = GetUtcNowIso8601();
        var view = new TrendViewEntity
        {
            PlcConnectionId = plcConnectionId,
            Name = AppConstants.DefaultTrendViewName,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        await GetConnection().InsertAsync(view).ConfigureAwait(false);
        return view;
    }

    public async Task<TrendViewEntity?> GetViewAsync(int trendViewId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetConnection().FindAsync<TrendViewEntity>(trendViewId).ConfigureAwait(false);
    }

    public async Task UpdateViewSettingsAsync(TrendViewEntity view)
    {
        ArgumentNullException.ThrowIfNull(view);
        await EnsureInitializedAsync().ConfigureAwait(false);
        view.UpdatedAt = GetUtcNowIso8601();
        await GetConnection().UpdateAsync(view).ConfigureAwait(false);
    }

    public async Task<int> SetAllSampleIntervalMsAsync(int sampleIntervalMs)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        sampleIntervalMs = TrendRangeHelper.ClampSampleIntervalMs(sampleIntervalMs);
        var now = GetUtcNowIso8601();
        return await GetConnection().ExecuteAsync(
            """
            UPDATE trend_views
            SET sample_interval_ms = ?, updated_at = ?;
            """,
            sampleIntervalMs,
            now).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrendViewItemEntity>> GetViewItemsAsync(int trendViewId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var items = await GetConnection().Table<TrendViewItemEntity>()
            .Where(item => item.TrendViewId == trendViewId)
            .OrderBy(item => item.SortOrder)
            .ToListAsync()
            .ConfigureAwait(false);
        return items;
    }

    public async Task AddItemToViewAsync(int trendViewId, int itemId, string? color = null)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        if (await ViewContainsItemAsync(trendViewId, itemId).ConfigureAwait(false))
        {
            throw new InvalidOperationException("变量已在趋势图中。");
        }

        var maxOrder = await GetConnection().ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(sort_order), -1) FROM trend_view_items WHERE trend_view_id = ?;",
            trendViewId).ConfigureAwait(false);

        await GetConnection().InsertAsync(new TrendViewItemEntity
        {
            TrendViewId = trendViewId,
            ItemId = itemId,
            DisplayColor = color,
            SortOrder = maxOrder + 1
        }).ConfigureAwait(false);
    }

    public async Task RemoveItemFromViewAsync(int trendViewId, int itemId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await GetConnection().ExecuteAsync(
            "DELETE FROM trend_view_items WHERE trend_view_id = ? AND item_id = ?;",
            trendViewId,
            itemId).ConfigureAwait(false);
    }

    public async Task<bool> ViewContainsItemAsync(int trendViewId, int itemId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var count = await GetConnection().ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM trend_view_items WHERE trend_view_id = ? AND item_id = ?;",
            trendViewId,
            itemId).ConfigureAwait(false);
        return count > 0;
    }

    public async ValueTask DisposeAsync()
    {
        _initializationLock.Dispose();
        if (_dbConnection != null)
        {
            await _dbConnection.CloseAsync().ConfigureAwait(false);
            _dbConnection = null;
        }
    }

    private SQLiteAsyncConnection GetConnection() =>
        _dbConnection ?? throw new InvalidOperationException("数据库尚未初始化。");

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            SqliteNative.Initialize();

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _dbConnection = new SQLiteAsyncConnection(DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
            await _dbConnection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);
            await SchemaMigrator.EnsureSchemaAsync(_dbConnection).ConfigureAwait(false);
            await MigrateLegacyPasswordKeyPrefixesAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task ClearExistingDefaultFlagAsync(int? exceptId = null)
    {
        var connections = await GetConnection().Table<PlcConnectionEntity>().ToListAsync().ConfigureAwait(false);
        foreach (var connection in connections.Where(item => item.IsDefault && item.Id != exceptId))
        {
            connection.IsDefault = false;
            await GetConnection().UpdateAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// E6：把 <see cref="LegacyPasswordKeyPrefixes"/> 中旧前缀键的密码搬到当前前缀键。
    /// 列表为空时直接返回（零开销）；搬移规则：新键已有值时仅删除旧键。
    /// </summary>
    private async Task MigrateLegacyPasswordKeyPrefixesAsync()
    {
        if (LegacyPasswordKeyPrefixes.Length == 0)
        {
            return;
        }

        var connections = await GetConnection().Table<PlcConnectionEntity>().ToListAsync().ConfigureAwait(false);
        foreach (var legacyPrefix in LegacyPasswordKeyPrefixes)
        {
            foreach (var connection in connections)
            {
                var legacyKey = $"{legacyPrefix}{connection.Id}";
                var legacyPassword = await _credentialStore.GetAsync(legacyKey).ConfigureAwait(false);
                if (string.IsNullOrEmpty(legacyPassword))
                {
                    continue;
                }

                var currentKey = BuildSecureStorageKey(connection.Id);
                var currentPassword = await _credentialStore.GetAsync(currentKey).ConfigureAwait(false);
                if (string.IsNullOrEmpty(currentPassword))
                {
                    await _credentialStore.SetAsync(currentKey, legacyPassword).ConfigureAwait(false);
                }

                _credentialStore.Remove(legacyKey);
            }
        }
    }

    private static string DefaultDisplayFormat(string? dataType) =>
        DisplayFormatRegistry.GetDefaultFormatId(dataType);

    private static string BuildSecureStorageKey(int connectionId) =>
        $"{AppConstants.SecureStoragePasswordKeyPrefix}{connectionId}";

    private static string GetUtcNowIso8601() =>
        DateTimeOffset.UtcNow.ToString("O");

    private static string FormatTimestampUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");
}
