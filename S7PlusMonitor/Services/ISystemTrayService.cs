using System.Windows;

namespace S7PlusMonitor.Services;

public enum CloseWindowAction
{
    Ask,
    Tray,
    Exit
}

public interface IWindowCloseService
{
    void Attach(Window window);

    Task RequestExitAsync();

    bool IsExitInProgress { get; }
}

public interface ISystemTrayService : IDisposable
{
    void Initialize(Window mainWindow);

    void Show();

    void Hide();

    void UpdateTooltip(string text);

    bool IsVisible { get; }

    void RestoreMainWindow();
}
