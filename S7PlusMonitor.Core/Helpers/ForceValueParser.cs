using System.Globalization;
using System.Text.RegularExpressions;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>强制写入解析结果；成功时 <see cref="Value"/> 为可写入 PLC 的 CLR 类型。</summary>
public sealed class ForceValueParseResult
{
    /// <summary>是否解析成功。</summary>
    public bool IsSuccess { get; init; }

    /// <summary>解析得到的 CLR 值（bool/byte/ushort/uint/ulong/long/float/double/string）。</summary>
    public object? Value { get; init; }

    /// <summary>失败时的用户可读提示。</summary>
    public string? ErrorMessage { get; init; }

    public static ForceValueParseResult Success(object value) =>
        new() { IsSuccess = true, Value = value };

    public static ForceValueParseResult Failure(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}

/// <summary>监视表“强制值”输入框解析器：按 <see cref="ValueDisplayFormat"/> 将文本转为可写入 PLC 的值。</summary>
/// <remarks>支持 Hex 前缀 16#/0x、Bin 前缀 2#，以及下划线分隔；Float 兼容当前文化。</remarks>
public static partial class ForceValueParser
{
    /// <summary>按显示格式解析强制值文本。</summary>
    /// <param name="input">用户输入（可能含 16#/0x/2# 前缀）</param>
    /// <param name="formatId">显示格式 Id（如 "Hex"）</param>
    /// <param name="dataType">PLC 数据类型（如 "Word"）用于选择存储类型</param>
    public static ForceValueParseResult Parse(string? input, string? formatId, string? dataType)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ForceValueParseResult.Failure("请输入强制值。");
        }

        var format = DisplayFormatRegistry.ParseFormatOrDefault(formatId, dataType);
        var trimmed = input.Trim();

        return format switch
        {
            ValueDisplayFormat.Bool => ParseBool(trimmed),
            ValueDisplayFormat.Hex => ParseHex(trimmed, dataType),
            ValueDisplayFormat.Dec => ParseDec(trimmed, signed: false, dataType),
            ValueDisplayFormat.DecSigned => ParseDec(trimmed, signed: true, dataType),
            ValueDisplayFormat.Bin => ParseBin(trimmed, dataType),
            ValueDisplayFormat.Float => ParseFloat(trimmed, dataType),
            ValueDisplayFormat.String => ForceValueParseResult.Success(Unquote(trimmed)),
            _ => ForceValueParseResult.Failure("不支持的显示格式。")
        };
    }

    private static ForceValueParseResult ParseBool(string input)
    {
        if (string.Equals(input, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "1", StringComparison.Ordinal))
        {
            return ForceValueParseResult.Success(true);
        }

        if (string.Equals(input, "FALSE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "0", StringComparison.Ordinal))
        {
            return ForceValueParseResult.Success(false);
        }

        return ForceValueParseResult.Failure("BOOL 强制值应为 TRUE 或 FALSE。");
    }

    private static ForceValueParseResult ParseHex(string input, string? dataType)
    {
        var text = input;
        if (text.StartsWith("16#", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..];
        }
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        text = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!HexRegex().IsMatch(text))
        {
            return ForceValueParseResult.Failure("Hex 格式无效，示例：16#1234");
        }

        if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return ForceValueParseResult.Failure("Hex 数值超出范围。");
        }

        return ForceValueParseResult.Success(MapToClrUnsignedType(value, dataType));
    }

    private static ForceValueParseResult ParseDec(string input, bool signed, string? dataType)
    {
        if (signed)
        {
            if (!long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
            {
                return ForceValueParseResult.Failure("Dec+/- 格式无效。");
            }

            if (DisplayFormatRegistry.ClassifyDataType(dataType) == PlcValueKind.UnsignedInteger
                && signedValue < 0)
            {
                return ForceValueParseResult.Failure("无符号类型不支持负数。");
            }

            return ForceValueParseResult.Success(MapToClrSignedType(signedValue, dataType));
        }

        if (!ulong.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue))
        {
            return ForceValueParseResult.Failure("Dec 格式无效。");
        }

        return ForceValueParseResult.Success(MapToClrUnsignedType(unsignedValue, dataType));
    }

    private static ForceValueParseResult ParseBin(string input, string? dataType)
    {
        var text = input;
        if (text.StartsWith("2#", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        text = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!BinRegex().IsMatch(text))
        {
            return ForceValueParseResult.Failure("Bin 格式无效，示例：2#1010");
        }

        try
        {
            var value = Convert.ToUInt64(text, 2);
            return ForceValueParseResult.Success(MapToClrUnsignedType(value, dataType));
        }
        catch
        {
            return ForceValueParseResult.Failure("Bin 数值超出范围。");
        }
    }

    private static ForceValueParseResult ParseFloat(string input, string? dataType)
    {
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return ForceValueParseResult.Failure("Float 格式无效。");
        }

        return DisplayFormatRegistry.ClassifyDataType(dataType) switch
        {
            PlcValueKind.Float => ForceValueParseResult.Success((float)value),
            PlcValueKind.Double => ForceValueParseResult.Success(value),
            _ => ForceValueParseResult.Success(value)
        };
    }

    /// <summary>按目标 PLC 类型将无符号数值映射为 CLR 存储类型。</summary>
    private static object MapToClrUnsignedType(ulong value, string? dataType) =>
        DisplayFormatRegistry.ClassifyDataType(dataType) switch
        {
            PlcValueKind.Bool => value != 0,
            PlcValueKind.Byte => (byte)value,
            PlcValueKind.Word => (ushort)value,
            PlcValueKind.DWord => (uint)value,
            PlcValueKind.LWord => value,
            PlcValueKind.SignedInteger => unchecked((long)value),
            PlcValueKind.UnsignedInteger => value,
            _ => value
        };

    /// <summary>按目标 PLC 类型将有符号数值映射为 CLR 存储类型。</summary>
    private static object MapToClrSignedType(long value, string? dataType) =>
        DisplayFormatRegistry.ClassifyDataType(dataType) switch
        {
            PlcValueKind.Bool => value != 0,
            PlcValueKind.Byte => (byte)value,
            PlcValueKind.Word => (short)value,
            PlcValueKind.DWord => (int)value,
            PlcValueKind.LWord => value,
            PlcValueKind.SignedInteger => value,
            PlcValueKind.UnsignedInteger => (ulong)value,
            _ => value
        };

    private static string Unquote(string input)
    {
        if (input.Length >= 2 && input.StartsWith('"') && input.EndsWith('"'))
        {
            return input[1..^1];
        }

        return input;
    }

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexRegex();

    [GeneratedRegex("^[01]+$")]
    private static partial Regex BinRegex();
}
