using System.Windows;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Pages;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

/// <summary>以独立窗口打开趋势图设置。</summary>
public static class TrendSettingsPopupService
{
    private static readonly object OpenPopupsLock = new();
    private static readonly List<Window> OpenPopups = [];

    public static async Task<TrendViewEntity?> ShowAsync(
        TrendViewEntity entity,
        ITrendViewRepository trendViewRepository,
        string? connectionProfileName = null,
        int pollIntervalMs = AppConstants.DefaultPollIntervalMs)
    {
        var settingsVm = new TrendSettingsViewModel(entity, trendViewRepository, connectionProfileName, pollIntervalMs);
        var completion = new TaskCompletionSource<TrendViewEntity?>();
        Window? popupWindow = null;

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            popupWindow = CreatePopupWindow(settingsVm, connectionProfileName);

            void Complete(TrendViewEntity? savedEntity)
            {
                completion.TrySetResult(savedEntity);
            }

            settingsVm.CloseRequested += (_, saved) =>
            {
                Complete(saved ? settingsVm.SavedEntity : null);
                popupWindow.Close();
            };

            popupWindow.Closed += (_, _) =>
            {
                lock (OpenPopupsLock)
                {
                    OpenPopups.Remove(popupWindow);
                }

                Complete(settingsVm.WasSaved ? settingsVm.SavedEntity : null);
            };

            lock (OpenPopupsLock)
            {
                OpenPopups.Add(popupWindow);
            }

            popupWindow.Owner = Application.Current?.MainWindow;
            popupWindow.Show();
        }).ConfigureAwait(true);

        return await completion.Task.ConfigureAwait(true);
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

    private static Window CreatePopupWindow(TrendSettingsViewModel settingsVm, string? connectionProfileName)
    {
        var title = string.IsNullOrWhiteSpace(connectionProfileName)
            ? "趋势图设置"
            : $"趋势图设置 · {connectionProfileName}";

        return new Window
        {
            Title = title,
            Width = UiLayoutMetrics.TrendSettingsPopupWidth,
            Height = UiLayoutMetrics.TrendSettingsPopupHeight,
            MinWidth = UiLayoutMetrics.TrendSettingsPopupMinWidth,
            MinHeight = UiLayoutMetrics.TrendSettingsPopupMinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Application.Current?.TryFindResource("PageBackgroundBrush") as System.Windows.Media.Brush,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Segoe UI Emoji"),
            ShowInTaskbar = false,
            Content = new TrendSettingsPage(settingsVm)
        };
    }
}
