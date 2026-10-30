using System.Windows;

namespace S7PlusMonitor.Services;

/// <summary>确认/提示对话框（在 UI 线程弹出）。</summary>
internal static class UiDialog
{
    public static async Task AlertAsync(string title, string message)
    {
        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            var owner = Application.Current?.MainWindow;
            if (owner is { IsVisible: true })
            {
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }).ConfigureAwait(true);
    }

    public static async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var confirmed = false;
        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            var owner = Application.Current?.MainWindow;
            var result = owner is { IsVisible: true }
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            confirmed = result == MessageBoxResult.Yes;
        }).ConfigureAwait(true);

        _ = accept;
        _ = cancel;
        return confirmed;
    }
}
