using System.Windows.Controls;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Pages;

public partial class CloseChoicePage : UserControl
{
    public CloseChoicePage(CloseChoiceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
