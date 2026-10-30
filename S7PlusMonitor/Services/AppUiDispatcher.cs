using System.Windows;
using System.Windows.Threading;

namespace S7PlusMonitor.Services;

/// <summary>退出阶段安全调度 UI；轮询刷新使用 DataBind 优先级，避免抢占输入。</summary>
internal static class AppUiDispatcher
{
    private static Dispatcher? DispatcherOrNull => Application.Current?.Dispatcher;

    public static bool CheckAccess()
    {
        var dispatcher = DispatcherOrNull;
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public static void InvokeSafe(Action action)
    {
        try
        {
            var dispatcher = DispatcherOrNull;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static void InvokeSafeAsync(Action action) =>
        _ = InvokeSafeAwaitAsync(action);

    public static void InvokeSafeAsync(Func<Task> action) =>
        _ = InvokeSafeAwaitAsync(action);

    public static Task InvokeSafeAwaitAsync(Action action) =>
        InvokeSafeAwaitAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    public static async Task InvokeSafeAwaitAsync(Func<Task> action)
    {
        try
        {
            var dispatcher = DispatcherOrNull;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                await action().ConfigureAwait(true);
                return;
            }

            await dispatcher.InvokeAsync(action, DispatcherPriority.DataBind).Task.Unwrap().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
