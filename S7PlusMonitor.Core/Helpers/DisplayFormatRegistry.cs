namespace S7PlusMonitor.Core.Helpers;

/// <summary>显示格式注册表：根据 PLC 数据类型决定允许的显示格式、默认格式及存储类型映射。</summary>
/// <remarks>对齐 TIA Portal 类型体系；<see cref="ClassifyDataType"/> 为核心分类，<see cref="GetAllowedFormats"/> 决定 Picker 可选项。</remarks>
public static class DisplayFormatRegistry
{
    private static readonly ValueDisplayFormat[] BoolFormats =
    [
        ValueDisplayFormat.Bool,
        ValueDisplayFormat.Hex,
        ValueDisplayFormat.Dec,
        ValueDisplayFormat.DecSigned,
        ValueDisplayFormat.Bin
    ];

    private static readonly ValueDisplayFormat[] IntegerFormats =
    [
        ValueDisplayFormat.Dec,
        ValueDisplayFormat.DecSigned,
        ValueDisplayFormat.Hex,
        ValueDisplayFormat.Bin
    ];

    private static readonly ValueDisplayFormat[] WordFormats =
    [
        ValueDisplayFormat.Hex,
        ValueDisplayFormat.Dec,
        ValueDisplayFormat.DecSigned,
        ValueDisplayFormat.Bin
    ];

    private static readonly ValueDisplayFormat[] FloatFormats =
    [
        ValueDisplayFormat.Float,
        ValueDisplayFormat.Hex,
        ValueDisplayFormat.Dec,
        ValueDisplayFormat.DecSigned
    ];

    private static readonly ValueDisplayFormat[] StringFormats = [ValueDisplayFormat.String];

    private static readonly Dictionary<ValueDisplayFormat, string> FormatLabels = new()
    {
        [ValueDisplayFormat.Bool] = "Bool",
        [ValueDisplayFormat.Hex] = "Hex",
        [ValueDisplayFormat.Dec] = "Dec",
        [ValueDisplayFormat.DecSigned] = "Dec+/-",
        [ValueDisplayFormat.Bin] = "Bin",
        [ValueDisplayFormat.Float] = "Float",
        [ValueDisplayFormat.String] = "String"
    };

    /// <summary>获取指定数据类型允许的显示格式列表。</summary>
    public static IReadOnlyList<ValueDisplayFormat> GetAllowedFormats(string? dataType)
    {
        return ClassifyDataType(dataType) switch
        {
            PlcValueKind.Bool => BoolFormats,
            PlcValueKind.Byte or PlcValueKind.Word or PlcValueKind.DWord or PlcValueKind.LWord => WordFormats,
            PlcValueKind.SignedInteger or PlcValueKind.UnsignedInteger => IntegerFormats,
            PlcValueKind.Float or PlcValueKind.Double => FloatFormats,
            PlcValueKind.String => StringFormats,
            _ => WordFormats
        };
    }

    public static string GetDefaultFormatId(string? dataType) =>
        ToFormatId(GetDefaultFormat(dataType));

    /// <summary>获取指定类型的默认显示格式（对齐 TIA）。</summary>
    public static ValueDisplayFormat GetDefaultFormat(string? dataType) =>
        ClassifyDataType(dataType) switch
        {
            PlcValueKind.Bool => ValueDisplayFormat.Bool,
            PlcValueKind.SignedInteger or PlcValueKind.UnsignedInteger => ValueDisplayFormat.Dec,
            PlcValueKind.Float or PlcValueKind.Double => ValueDisplayFormat.Float,
            PlcValueKind.String => ValueDisplayFormat.String,
            _ => ValueDisplayFormat.Hex
        };

    public static bool TryParseFormat(string? formatId, out ValueDisplayFormat format)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            format = ValueDisplayFormat.Hex;
            return false;
        }

        return Enum.TryParse(formatId.Trim(), ignoreCase: true, out format);
    }

    public static ValueDisplayFormat ParseFormatOrDefault(string? formatId, string? dataType)
    {
        if (!TryParseFormat(formatId, out var parsed))
        {
            return GetDefaultFormat(dataType);
        }

        var allowed = GetAllowedFormats(dataType);
        return allowed.Contains(parsed) ? parsed : GetDefaultFormat(dataType);
    }

    public static string ToFormatId(ValueDisplayFormat format) => format.ToString();

    public static string ToDisplayLabel(ValueDisplayFormat format) =>
        FormatLabels.TryGetValue(format, out var label) ? label : format.ToString();

    public static IReadOnlyList<string> GetAllowedFormatIds(string? dataType) =>
        GetAllowedFormats(dataType).Select(ToFormatId).ToArray();

    public static bool IsBoolType(string? dataType) => ClassifyDataType(dataType) == PlcValueKind.Bool;

    /// <summary>BOOL 类型且显示格式为 Bool 时，强制列仅显示「写入/反转」按钮。</summary>
    public static bool UsesBoolForceMode(string? dataType, string? formatId) =>
        IsBoolType(dataType) && ParseFormatOrDefault(formatId, dataType) == ValueDisplayFormat.Bool;

    /// <summary>按 PLC 数据类型分类为 <see cref="PlcValueKind"/>，用于决定可用格式与存储映射。</summary>
    public static PlcValueKind ClassifyDataType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return PlcValueKind.Unknown;
        }

        var normalized = dataType.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Equals("Bool", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("BBool", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.Bool;
        }

        if (normalized.Equals("Byte", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Bcd8", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.Byte;
        }

        if (normalized.Equals("Word", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Bcd16", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Counter", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.Word;
        }

        if (normalized.Equals("DWord", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Bcd32", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("TimeOfDay", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("LTimeOfDay", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.DWord;
        }

        if (normalized.Equals("LWord", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Bcd64", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.LWord;
        }

        if (normalized is "SInt" or "Int" or "DInt" or "LInt")
        {
            return PlcValueKind.SignedInteger;
        }

        if (normalized is "USInt" or "UInt" or "UDInt" or "ULInt")
        {
            return PlcValueKind.UnsignedInteger;
        }

        if (normalized.Equals("Real", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.Float;
        }

        if (normalized.Equals("LReal", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.Double;
        }

        if (normalized.Contains("String", StringComparison.OrdinalIgnoreCase))
        {
            return PlcValueKind.String;
        }

        return PlcValueKind.Unknown;
    }

    /// <summary>获取 Hex 显示时的固定位数（补零）。</summary>
    public static int GetHexDigitCount(string? dataType) =>
        ClassifyDataType(dataType) switch
        {
            PlcValueKind.Bool => 1,
            PlcValueKind.Byte => 2,
            PlcValueKind.Word => 4,
            PlcValueKind.DWord => 8,
            PlcValueKind.LWord => 16,
            PlcValueKind.SignedInteger or PlcValueKind.UnsignedInteger => 8,
            _ => 8
        };
}
