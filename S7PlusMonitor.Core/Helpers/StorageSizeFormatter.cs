namespace S7PlusMonitor.Core.Helpers;

/// <summary>存储大小格式化：1024 进制，自动切换 B/KB/MB/GB，保留 1–2 位小数。</summary>
public static class StorageSizeFormatter
{
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024):0.##} MB";
        }

        return $"{bytes / (1024d * 1024 * 1024):0.##} GB";
    }
}
