namespace S7PlusMonitor.Core.Helpers;

public static class TrayTooltipHelper
{
    public const int WindowsShellMaxChars = 127;

    /// <summary>
    /// Windows NOTIFYICONDATA szTip：NOTIFYICON_VERSION_4 下用 \r\n 换行。
    /// </summary>
    public static string FormatForWindowsShell(string text, int maxChars = WindowsShellMaxChars)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return "S7Plus 监视器";
        }

        var selected = new List<string>(lines.Length);
        var length = 0;
        foreach (var line in lines)
        {
            var separatorLength = selected.Count > 0 ? 2 : 0;
            if (length + separatorLength + line.Length > maxChars)
            {
                break;
            }

            selected.Add(line);
            length += separatorLength + line.Length;
        }

        return selected.Count == 0
            ? lines[0][..Math.Min(lines[0].Length, maxChars)]
            : string.Join("\r\n", selected);
    }
}
