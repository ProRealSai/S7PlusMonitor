using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>PlcConnectionEntity 字段拷贝（用于在位刷新，避免 ObservableCollection 重建）。</summary>

public static class PlcConnectionEntityMapper
{
    public static void CopyFields(PlcConnectionEntity source, PlcConnectionEntity target)
    {
        target.Name = source.Name;
        target.Host = source.Host;
        target.Port = source.Port;
        target.TimeoutSeconds = source.TimeoutSeconds;
        target.Username = source.Username;
        target.SecurityMode = source.SecurityMode;
        target.IsDefault = source.IsDefault;
        target.SortOrder = source.SortOrder;
        target.LastUsedAt = source.LastUsedAt;
        target.UpdatedAt = source.UpdatedAt;
    }
}
