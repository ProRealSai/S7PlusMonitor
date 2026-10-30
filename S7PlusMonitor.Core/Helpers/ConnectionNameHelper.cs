namespace S7PlusMonitor.Core.Helpers;

/// <summary>连接档案名称生成：基于已存在名称去重，生成“新连接 2”等唯一名。</summary>
public static class ConnectionNameHelper
{
    public static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName = "新连接")
    {
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {DateTime.UtcNow:HHmmss}";
    }
}
