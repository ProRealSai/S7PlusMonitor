using System.Windows.Controls;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Pages;

public partial class TrendSettingsPage : UserControl
{
    public TrendSettingsPage(TrendSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
