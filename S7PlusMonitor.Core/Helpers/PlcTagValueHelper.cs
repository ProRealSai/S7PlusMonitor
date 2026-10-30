using System.Globalization;
using System.Reflection;
using S7CommPlusDriver.ClientApi;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>PLC Tag 值读写辅助：GetValue/SetValue 封装驱动反射。</summary>

internal static class PlcTagValueHelper
{
    /// <summary>通过反射取 <c>PlcTag.Value</c>（驱动版本间属性名一致，封装反射避免直接依赖）。</summary>
    public static object? GetValue(PlcTag tag)
    {
        var valueProperty = tag.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        return valueProperty?.GetValue(tag);
    }

    /// <summary>反射设置 <c>PlcTag.Value</c>，自动按目标类型转换（支持 bool 文本）。</summary>
    public static void SetValue(PlcTag tag, object value)
    {
        var valueProperty = tag.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"符号 {tag.Name} 的类型不支持写入。");

        var converted = ConvertToPropertyType(value, valueProperty.PropertyType);
        valueProperty.SetValue(tag, converted);
    }

    private static object? ConvertToPropertyType(object value, Type targetType)
    {
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(bool) && value is string boolText)
        {
            if (bool.TryParse(boolText, out var parsedBool))
            {
                return parsedBool;
            }

            if (string.Equals(boolText, "TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(boolText, "FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture)!, ignoreCase: true);
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
