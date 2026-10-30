using System.Windows.Controls;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Pages;

public partial class BrowsePage : UserControl
{
    public BrowsePage(BrowseViewModel viewModel, AppSessionViewModel session)
    {
        InitializeComponent();
        DataContext = viewModel;
        StatusBar.DataContext = session;
    }
}
