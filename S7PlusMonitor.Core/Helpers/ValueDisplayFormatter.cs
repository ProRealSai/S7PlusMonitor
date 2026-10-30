using System.Globalization;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>当前值格式化：按 DisplayFormat 将 object 转为 TRUE/FALSE 或 Hex/Dec 等。</summary>

public static class ValueDisplayFormatter
{
    public static string Format(object? rawValue, string? formatId, string? dataType)
    {
        if (rawValue == null)
        {
            return "—";
        }

        var format = DisplayFormatRegistry.ParseFormatOrDefault(formatId, dataType);
        return format switch
        {
            ValueDisplayFormat.Bool => FormatBool(rawValue),
            ValueDisplayFormat.Hex => FormatHex(rawValue, dataType),
            ValueDisplayFormat.Dec => FormatDec(rawValue, signed: false),
            ValueDisplayFormat.DecSigned => FormatDec(rawValue, signed: true),
            ValueDisplayFormat.Bin => FormatBin(rawValue, dataType),
            ValueDisplayFormat.Float => FormatFloat(rawValue),
            ValueDisplayFormat.String => FormatString(rawValue),
            _ => rawValue.ToString() ?? "—"
        };
    }

    public static string FormatBool(object value) =>
        Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "TRUE" : "FALSE";

    public static string FormatHex(object value, string? dataType)
    {
        if (value is bool boolean)
        {
            return boolean ? "16#1" : "16#0";
        }

        if (value is string)
        {
            return $"\"{value}\"";
        }

        if (value is float or double)
        {
            var bits = value is float single
                ? BitConverter.SingleToUInt32Bits(single)
                : BitConverter.DoubleToUInt64Bits((double)value);

            var digitCount = DisplayFormatRegistry.GetHexDigitCount(dataType);
            return digitCount <= 8
                ? FormatHexValue((ulong)(uint)bits, digitCount)
                : FormatHexValue(bits, digitCount);
        }

        if (!TryConvertToUnsigned(value, out var unsigned))
        {
            return value.ToString() ?? "—";
        }

        return FormatHexValue(unsigned, DisplayFormatRegistry.GetHexDigitCount(dataType));
    }

    public static string FormatDec(object value, bool signed)
    {
        if (value is bool boolean)
        {
            return boolean ? "1" : "0";
        }

        if (value is string text)
        {
            return text;
        }

        if (value is float or double)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture)
                .ToString("G", CultureInfo.InvariantCulture);
        }

        if (signed && TryConvertToSigned(value, out var signedValue))
        {
            return signedValue.ToString(CultureInfo.InvariantCulture);
        }

        if (TryConvertToUnsigned(value, out var unsigned))
        {
            return unsigned.ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? "—";
    }

    public static string FormatBin(object value, string? dataType)
    {
        if (value is bool boolean)
        {
            return boolean ? "2#1" : "2#0";
        }

        if (!TryConvertToUnsigned(value, out var unsigned))
        {
            return value.ToString() ?? "—";
        }

        var bitCount = DisplayFormatRegistry.GetHexDigitCount(dataType) * 4;
        var bits = Convert.ToString((long)unsigned, 2).PadLeft(bitCount, '0');
        return $"2#{GroupBinary(bits)}";
    }

    public static string FormatFloat(object value)
    {
        if (value is string text)
        {
            return text;
        }

        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return number.ToString("G9", CultureInfo.InvariantCulture);
    }

    public static string FormatString(object value) => $"\"{value}\"";

    public static string GetForcePlaceholder(string? formatId, string? dataType)
    {
        var format = DisplayFormatRegistry.ParseFormatOrDefault(formatId, dataType);
        return format switch
        {
            ValueDisplayFormat.Hex => "16#____",
            ValueDisplayFormat.Dec or ValueDisplayFormat.DecSigned => "0",
            ValueDisplayFormat.Bin => "2#____",
            ValueDisplayFormat.Float => "0.0",
            ValueDisplayFormat.String => "文本",
            _ => string.Empty
        };
    }

    private static string FormatHexValue(ulong value, int digitCount)
    {
        var hex = value.ToString($"X{digitCount}", CultureInfo.InvariantCulture);
        if (digitCount <= 4)
        {
            return $"16#{hex}";
        }

        var grouped = string.Join("_", Chunk(hex, 4));
        return $"16#{grouped}";
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var index = 0; index < text.Length; index += size)
        {
            yield return text.Substring(index, Math.Min(size, text.Length - index));
        }
    }

    private static string GroupBinary(string bits)
    {
        if (bits.Length <= 4)
        {
            return bits;
        }

        var groups = new List<string>();
        for (var index = bits.Length; index > 0; index -= 4)
        {
            var start = Math.Max(0, index - 4);
            groups.Add(bits.Substring(start, index - start));
        }

        groups.Reverse();
        return string.Join("_", groups);
    }

    private static bool TryConvertToUnsigned(object value, out ulong unsigned)
    {
        unsigned = 0;
        try
        {
            unsigned = value switch
            {
                byte b => b,
                sbyte sb => (ulong)sb,
                short s => (ulong)s,
                ushort us => us,
                int i => (ulong)i,
                uint ui => ui,
                long l => (ulong)l,
                ulong ul => ul,
                _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertToSigned(object value, out long signed)
    {
        signed = 0;
        try
        {
            signed = value switch
            {
                byte b => b,
                sbyte sb => sb,
                short s => s,
                ushort us => us,
                int i => i,
                uint ui => ui,
                long l => l,
                ulong ul when ul <= long.MaxValue => (long)ul,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
}
