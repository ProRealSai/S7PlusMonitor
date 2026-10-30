namespace S7PlusMonitor.Core.Helpers;

public enum PlcConnectionSaveAction
{
    Create,
    Update,
    Reject
}

public readonly record struct PlcConnectionSavePlan(
    PlcConnectionSaveAction Action,
    int? UpdateId,
    string? ErrorMessage);

/// <summary>连接档案保存目标判定：同名不得覆盖另一条档案；端口/超时校验。</summary>
public static class PlcConnectionSavePlanner
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    public static bool IsValidTimeoutSeconds(int timeoutSeconds) => timeoutSeconds > 0;

    public static PlcConnectionSavePlan Plan(int? selectedId, int? existingByNameId, string? existingByName)
    {
        if (existingByNameId is int occupiedId && occupiedId != selectedId)
        {
            var label = string.IsNullOrWhiteSpace(existingByName) ? occupiedId.ToString() : existingByName.Trim();
            return new PlcConnectionSavePlan(
                PlcConnectionSaveAction.Reject,
                null,
                $"保存失败：名称「{label}」已被使用。");
        }

        if (selectedId is > 0)
        {
            return new PlcConnectionSavePlan(PlcConnectionSaveAction.Update, selectedId, null);
        }

        return new PlcConnectionSavePlan(PlcConnectionSaveAction.Create, null, null);
    }
}
