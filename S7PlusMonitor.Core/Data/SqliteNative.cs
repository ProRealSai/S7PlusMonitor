namespace S7PlusMonitor.Core.Data;

/// <summary>SQLitePCLRaw 3.x 原生库初始化（替代 2.x 的 <c>Batteries_V2.Init</c>）。</summary>
public static class SqliteNative
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
    }
}
