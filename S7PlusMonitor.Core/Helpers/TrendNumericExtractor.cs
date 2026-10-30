using System.Globalization;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>趋势数值提取：将 VariableReadResult 转为可绘图 double/bool。</summary>

public static class TrendNumericExtractor
{
    public static bool TryExtract(VariableReadResult result, out double value)
    {
        value = 0;
        if (result.Quality != VariableReadQuality.Good || result.Value == null)
        {
            return false;
        }

        return TryExtractRaw(result.Value, out value);
    }

    public static bool TryExtractRaw(object rawValue, out double value)
    {
        value = 0;
        try
        {
            value = rawValue switch
            {
                bool boolean => boolean ? 1d : 0d,
                string text when bool.TryParse(text, out var boolean) => boolean ? 1d : 0d,
                string text when text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) => 1d,
                string text when text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) => 0d,
                byte b => b,
                sbyte sb => sb,
                short s => s,
                ushort us => us,
                int i => i,
                uint ui => ui,
                long l => l,
                ulong ul => ul,
                float f => f,
                double d => d,
                decimal m => (double)m,
                string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => Convert.ToDouble(rawValue, CultureInfo.InvariantCulture)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
}
