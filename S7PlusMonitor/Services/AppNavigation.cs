namespace S7PlusMonitor.Services;

/// <summary>主窗口 Tab 导航（替代 MAUI Shell 路由）。</summary>
internal static class AppNavigation
{
    public static Action<string>? RequestTab { get; set; }

    public static Task GoToAsync(string route)
    {
        var tab = route.Trim().Trim('/');
        RequestTab?.Invoke(tab);
        return Task.CompletedTask;
    }
}
