using System.Windows;
using System.Windows.Controls;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Pages;

public partial class MonitorPage : UserControl
{
    private readonly MonitorPageViewModel _viewModel;

    public MonitorPage(MonitorPageViewModel viewModel, AppSessionViewModel session)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        StatusBar.DataContext = session;
        TrendPanel.DataContext = viewModel.TrendWorkspace;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MonitorPageViewModel.IsMonitorPanelExpanded))
            {
                ApplyLayout();
            }
        };
        _viewModel.TrendWorkspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrendWorkspaceViewModel.IsExpanded))
            {
                ApplyLayout();
            }
        };

        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        var monitorExpanded = _viewModel.IsMonitorPanelExpanded;
        var trendExpanded = _viewModel.TrendWorkspace.IsExpanded;
        PanelsGrid.RowDefinitions.Clear();

        if (monitorExpanded && trendExpanded)
        {
            PanelsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            PanelsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            PanelsGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = monitorExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });
            PanelsGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = trendExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });
        }
    }
}
