using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace S7PlusMonitor.ViewModels;

/// <summary>关闭选择弹窗 VM（询问/托盘/退出）。</summary>

public partial class CloseChoiceViewModel : ObservableObject
{
    private readonly TaskCompletionSource<CloseChoiceResult?> _completion = new();

    public Task<CloseChoiceResult?> ResultTask => _completion.Task;

    [RelayCommand]
    private void MinimizeToTray() =>
        Complete(new CloseChoiceResult(CloseChoiceAction.MinimizeToTray));

    [RelayCommand]
    private void ExitApplication() =>
        Complete(new CloseChoiceResult(CloseChoiceAction.ExitApplication));

    [RelayCommand]
    private void Cancel() => Complete(null);

    private void Complete(CloseChoiceResult? result)
    {
        if (!_completion.Task.IsCompleted)
        {
            _completion.TrySetResult(result);
        }
    }
}

public enum CloseChoiceAction
{
    MinimizeToTray,
    ExitApplication
}

public sealed record CloseChoiceResult(CloseChoiceAction Action);
