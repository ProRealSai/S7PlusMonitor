using System.Windows;
using S7PlusMonitor.Pages;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

public static class CloseChoicePopupService
{
    private const double PopupWidth = 420;
    private static readonly object OpenPopupsLock = new();
    private static readonly List<Window> OpenPopups = [];

    public static async Task<CloseChoiceResult?> ShowAsync()
    {
        var viewModel = new CloseChoiceViewModel();
        var completion = viewModel.ResultTask;

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            var popupWindow = new Window
            {
                Title = "关闭 S7Plus 监视器",
                Width = PopupWidth,
                SizeToContent = SizeToContent.Height,
                MinWidth = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current?.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = Application.Current?.TryFindResource("PageBackgroundBrush") as System.Windows.Media.Brush,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Segoe UI Emoji"),
                Content = new CloseChoicePage(viewModel)
            };

            popupWindow.Closed += (_, _) =>
            {
                lock (OpenPopupsLock)
                {
                    OpenPopups.Remove(popupWindow);
                }

                if (!completion.IsCompleted)
                {
                    viewModel.CancelCommand.Execute(null);
                }
            };

            lock (OpenPopupsLock)
            {
                OpenPopups.Add(popupWindow);
            }

            popupWindow.Show();
        }).ConfigureAwait(true);

        var result = await completion.ConfigureAwait(true);

        AppUiDispatcher.InvokeSafe(() =>
        {
            Window[] windows;
            lock (OpenPopupsLock)
            {
                windows = OpenPopups.ToArray();
            }

            foreach (var window in windows)
            {
                try
                {
                    window.Close();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });

        return result;
    }

    public static void CloseAll()
    {
        Window[] windows;
        lock (OpenPopupsLock)
        {
            windows = OpenPopups.ToArray();
        }

        AppUiDispatcher.InvokeSafe(() =>
        {
            foreach (var window in windows)
            {
                try
                {
                    window.Close();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });
    }
}
