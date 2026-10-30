namespace S7PlusMonitor.Core.Helpers;

/// <summary>
/// 解析 PLC 符号路径。TIA 中 DB 块符号常为 <c>11.数据存储</c>（编号.名称），
/// 不能简单按 <c>.</c> 拆成 <c>11</c> 与 <c>数据存储</c>。
/// </summary>
public static class SymbolPathTokenizer
{
    public static IReadOnlyList<string> Split(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        var raw = Tokenize(symbol.Trim());
        if (raw.Count >= 2 && IsDbNumber(raw[0]) && ContainsNonDigit(raw[1]))
        {
            var merged = new List<string>(raw.Count - 1) { $"{raw[0]}.{raw[1]}" };
            for (var i = 2; i < raw.Count; i++)
            {
                merged.Add(raw[i]);
            }

            return merged;
        }

        return raw;
    }

    public static string GetLeafDisplayName(string symbol)
    {
        var segments = Split(symbol);
        return segments.Count > 0 ? segments[^1] : symbol;
    }

    private static List<string> Tokenize(string symbol)
    {
        var segments = new List<string>();
        var index = 0;

        while (index < symbol.Length)
        {
            if (symbol[index] == '.')
            {
                index++;
                continue;
            }

            if (symbol[index] == '"')
            {
                index++;
                var start = index;
                while (index < symbol.Length && symbol[index] != '"')
                {
                    index++;
                }

                if (start < index)
                {
                    segments.Add(symbol[start..index]);
                }

                if (index < symbol.Length)
                {
                    index++;
                }

                continue;
            }

            var segmentStart = index;
            while (index < symbol.Length && symbol[index] != '.' && symbol[index] != '"')
            {
                index++;
            }

            var segment = symbol[segmentStart..index].Trim();
            if (segment.Length > 0)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private static bool IsDbNumber(string segment) =>
        segment.Length > 0 && segment.All(char.IsDigit);

    private static bool ContainsNonDigit(string segment) =>
        segment.Any(static ch => !char.IsDigit(ch));
}
