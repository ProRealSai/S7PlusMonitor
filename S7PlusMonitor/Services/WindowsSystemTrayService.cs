using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Services;

/// <summary>WPF 系统托盘（Hardcodet NotifyIcon）。托盘必须用 16×16 的 GDI Icon，不能用 WPF ImageSource。</summary>
public sealed class WindowsSystemTrayService : ISystemTrayService
{
    private readonly AppSessionViewModel _session;
    private readonly IServiceProvider _services;
    private Window? _mainWindow;
    private TaskbarIcon? _icon;
    private bool _isVisible;
    private bool _disposed;

    public WindowsSystemTrayService(AppSessionViewModel session, IServiceProvider services)
    {
        _session = session;
        _services = services;
    }

    public bool IsVisible => _isVisible;

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _icon = new TaskbarIcon
        {
            ToolTipText = "S7Plus 监视器",
            Icon = LoadTrayIcon(),
            Visibility = Visibility.Visible
        };

        _icon.TrayMouseDoubleClick += (_, _) => RestoreMainWindow();
        _icon.ContextMenu = BuildMenu();
        _isVisible = true;
    }

    /// <summary>
    /// Windows 通知区只要 HICON。trayicon.ico 是 PNG 压缩 ICO，WPF BitmapFrame 解不出来会显示空白；
    /// 与原 MAUI 一样用 System.Drawing.Icon 取 16×16。
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        foreach (var path in EnumerateTrayIconPaths())
        {
            if (TryLoadIconFromFile(path, out var fromFile))
            {
                return fromFile;
            }
        }

        if (TryLoadIconFromPackUri("pack://application:,,,/Assets/trayicon.ico", out var fromPack))
        {
            return fromPack;
        }

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            var associated = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (associated != null)
            {
                return associated;
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static IEnumerable<string> EnumerateTrayIconPaths()
    {
        yield return System.IO.Path.Combine(AppContext.BaseDirectory, "trayicon.ico");
        yield return System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");
    }

    private static bool TryLoadIconFromFile(string path, out System.Drawing.Icon icon)
    {
        icon = null!;
        if (!System.IO.File.Exists(path))
        {
            return false;
        }

        using var stream = System.IO.File.OpenRead(path);
        return TryClone16x16(stream, out icon);
    }

    private static bool TryLoadIconFromPackUri(string uri, out System.Drawing.Icon icon)
    {
        icon = null!;
        try
        {
            var resource = Application.GetResourceStream(new Uri(uri, UriKind.Absolute));
            if (resource?.Stream is null)
            {
                return false;
            }

            using (resource.Stream)
            {
                return TryClone16x16(resource.Stream, out icon);
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryClone16x16(Stream stream, out System.Drawing.Icon icon)
    {
        icon = null!;
        try
        {
            using var loaded = new System.Drawing.Icon(stream, 16, 16);
            icon = (System.Drawing.Icon)loaded.Clone();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Show()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Visibility = Visibility.Visible;
        _isVisible = true;
    }

    public void Hide()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Visibility = Visibility.Collapsed;
        _isVisible = false;
    }

    public void UpdateTooltip(string text)
    {
        if (_icon is null)
        {
            return;
        }

        _icon.ToolTipText = string.IsNullOrWhiteSpace(text) ? "S7Plus 监视器" : text;
    }

    public void RestoreMainWindow()
    {
        AppUiDispatcher.InvokeSafe(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        _icon?.Dispose();
        _icon = null;
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var show = new System.Windows.Controls.MenuItem { Header = "显示主窗口" };
        show.Click += (_, _) => RestoreMainWindow();

        var disconnect = new System.Windows.Controls.MenuItem { Header = "断开 PLC" };
        disconnect.Click += (_, _) =>
        {
            if (_session.IsConnected)
            {
                _ = _session.DisconnectAsync();
            }
        };

        var exit = new System.Windows.Controls.MenuItem { Header = "退出 S7Plus 监视器" };
        exit.Click += (_, _) => _ = _services.GetRequiredService<IWindowCloseService>().RequestExitAsync();

        menu.Items.Add(show);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(disconnect);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exit);
        menu.Opened += (_, _) => disconnect.IsEnabled = _session.IsConnected;
        return menu;
    }
}
