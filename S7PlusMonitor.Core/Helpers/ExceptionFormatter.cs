using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
namespace S7PlusMonitor.Core.Helpers;

/// <summary>异常格式化：提取最内层消息，避免驱动堆栈泄露到 UI。</summary>

public static class ExceptionFormatter
{
    private static readonly (Regex Pattern, string Replacement)[] MessageReplacements =
    [
        (new Regex(@"\bConnection\b", RegexOptions.IgnoreCase), "连接"),
        (new Regex(@"\bConnect\b", RegexOptions.IgnoreCase), "连接"),
        (new Regex(@"\bDisconnected\b", RegexOptions.IgnoreCase), "已断开"),
        (new Regex(@"\bDisconnect\b", RegexOptions.IgnoreCase), "断开"),
        (new Regex(@"\bTimeout\b", RegexOptions.IgnoreCase), "超时"),
        (new Regex(@"\bTimed out\b", RegexOptions.IgnoreCase), "已超时"),
        (new Regex(@"\bFailed\b", RegexOptions.IgnoreCase), "失败"),
        (new Regex(@"\bFailure\b", RegexOptions.IgnoreCase), "失败"),
        (new Regex(@"\bError\b", RegexOptions.IgnoreCase), "错误"),
        (new Regex(@"\bInvalid\b", RegexOptions.IgnoreCase), "无效"),
        (new Regex(@"\bNot connected\b", RegexOptions.IgnoreCase), "未连接"),
        (new Regex(@"\bAlready connected\b", RegexOptions.IgnoreCase), "已经连接"),
        (new Regex(@"\bLegitimation\b", RegexOptions.IgnoreCase), "密码授权"),
        (new Regex(@"\bAuthentication\b", RegexOptions.IgnoreCase), "认证"),
        (new Regex(@"\bUnauthorized\b", RegexOptions.IgnoreCase), "未授权"),
        (new Regex(@"\bAccess denied\b", RegexOptions.IgnoreCase), "访问被拒绝"),
        (new Regex(@"\bSecurity mode\b", RegexOptions.IgnoreCase), "安全模式"),
        (new Regex(@"\bBrowse\b", RegexOptions.IgnoreCase), "浏览"),
        (new Regex(@"\bOperation\b", RegexOptions.IgnoreCase), "操作"),
        (new Regex(@"\bRequest\b", RegexOptions.IgnoreCase), "请求"),
        (new Regex(@"\bResponse\b", RegexOptions.IgnoreCase), "响应"),
        (new Regex(@"\bPassword\b", RegexOptions.IgnoreCase), "密码"),
        (new Regex(@"\bUsername\b", RegexOptions.IgnoreCase), "用户名"),
        (new Regex(@"\bWrite disabled\b", RegexOptions.IgnoreCase), "写入已禁用"),
        (new Regex(@"\bUnsupported\b", RegexOptions.IgnoreCase), "不支持"),
        (new Regex(@"\bUnavailable\b", RegexOptions.IgnoreCase), "不可用"),
        (new Regex(@"\bHost unreachable\b", RegexOptions.IgnoreCase), "主机不可达"),
        (new Regex(@"\bNo connection could be made\b", RegexOptions.IgnoreCase), "无法建立连接"),
        (new Regex(@"\bAn existing connection was forcibly closed\b", RegexOptions.IgnoreCase), "现有连接被远程主机强制关闭"),
        (new Regex(@"\bObject reference not set to an instance of an object\b", RegexOptions.IgnoreCase), "对象引用未设置为对象实例"),
    ];

    public static string Format(Exception? ex)
    {
        if (ex == null)
        {
            return "未知错误";
        }

        var builder = new StringBuilder();
        Exception? current = ex;
        var depth = 0;

        while (current != null && depth < 4)
        {
            if (depth > 0)
            {
                builder.Append("；原因：");
            }

            builder.Append(FormatSingle(current));
            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static string FormatSingle(Exception ex)
    {
        if (TryFormatCommunicationException(ex, out var communicationMessage))
        {
            return communicationMessage;
        }

        if (TryFormatSqliteException(ex, out var sqliteMessage))
        {
            return sqliteMessage;
        }

        var category = GetCategory(ex);
        var message = TranslateMessage(ex.Message);

        if (string.IsNullOrWhiteSpace(message))
        {
            return category;
        }

        return message.Contains(category, StringComparison.OrdinalIgnoreCase)
            ? message
            : $"{category}：{message}";
    }

    private static string GetCategory(Exception ex) =>
        ex switch
        {
            InvalidOperationException => "操作无效",
            IOException => "通信中断",
            SocketException => "网络错误",
            TimeoutException => "操作超时",
            _ => ex.GetType().Name switch
            {
                "S7CommPlusConnectionException" => "PLC 连接错误",
                "S7CommPlusLegitimationException" => "PLC 密码授权失败",
                "S7CommPlusTimeoutException" => "PLC 操作超时",
                "S7CommPlusWriteDisabledException" => "PLC 写入已禁用",
                "S7CommPlusUnsupportedSecurityModeException" => "PLC 安全模式不支持",
                "S7CommPlusCommunicationException" => "PLC 通信错误",
                _ => "错误"
            }
        };

    private static bool TryFormatCommunicationException(Exception ex, out string message)
    {
        message = string.Empty;
        if (ex.GetType().Name != "S7CommPlusCommunicationException")
        {
            return false;
        }

        var operation = ex.GetType().GetProperty("Operation", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(ex) as string;
        var errorCodeValue = ex.GetType().GetProperty("ErrorCode", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(ex);

        var operationText = string.IsNullOrWhiteSpace(operation) ? "通信" : TranslateMessage(operation);
        if (errorCodeValue is uint errorCode && errorCode != 0)
        {
            message = $"PLC 通信错误（{operationText}，错误码 0x{errorCode:X8}）";
            return true;
        }

        message = $"PLC 通信错误（{operationText}）";
        return true;
    }

    private static bool TryFormatSqliteException(Exception ex, out string message)
    {
        message = string.Empty;
        if (ex.GetType().FullName != "SQLite.SQLiteException")
        {
            return false;
        }

        var raw = ex.Message ?? string.Empty;
        if (raw.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
            && raw.Contains("plc_connections", StringComparison.OrdinalIgnoreCase)
            && (raw.Contains(".name", StringComparison.OrdinalIgnoreCase)
                || raw.Contains(".Name", StringComparison.OrdinalIgnoreCase)))
        {
            message = $"档案名称已被使用。{raw}";
            return true;
        }

        if (raw.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
            && (raw.Contains("idx_plc_connections_one_default", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("is_default", StringComparison.OrdinalIgnoreCase)))
        {
            message = $"只能有一条默认档案。{raw}";
            return true;
        }

        if (raw.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            message = $"数据已存在，违反唯一性约束。{raw}";
            return true;
        }

        message = string.IsNullOrWhiteSpace(raw) ? "数据库错误" : $"数据库错误：{raw}";
        return true;
    }

    private static string TranslateMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var translated = message.Trim();
        foreach (var (pattern, replacement) in MessageReplacements)
        {
            translated = pattern.Replace(translated, replacement);
        }

        return translated;
    }
}
