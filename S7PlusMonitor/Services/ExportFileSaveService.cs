using System.Windows;
using Microsoft.Win32;

namespace S7PlusMonitor.Services;

/// <summary>导出文件保存：系统选路对话框封装。</summary>
public static class ExportFileSaveService
{
    public static Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true
        };

        var ok = dialog.ShowDialog(Application.Current?.MainWindow) == true;
        return Task.FromResult(ok ? dialog.FileName : null);
    }
}
