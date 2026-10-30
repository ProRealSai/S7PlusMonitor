using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using S7PlusMonitor.ViewModels;

namespace S7PlusMonitor.Pages;

public partial class ConnectionPage : UserControl
{
    private ConnectionViewModel? _viewModel;
    private bool _pickerRefreshQueued;

    public ConnectionPage(ConnectionViewModel viewModel, AppSessionViewModel session)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        StatusBar.DataContext = session;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => QueueRefreshProfilePicker();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionViewModel.ConnectionPickerGeneration)
            or nameof(ConnectionViewModel.SelectedConnection)
            or nameof(ConnectionViewModel.IsCreatingNewProfile))
        {
            QueueRefreshProfilePicker();
        }
    }

    private void QueueRefreshProfilePicker()
    {
        if (_pickerRefreshQueued)
        {
            return;
        }

        _pickerRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(RefreshProfilePicker, DispatcherPriority.DataBind);
    }

    private void RefreshProfilePicker()
    {
        _pickerRefreshQueued = false;
        if (ProfileCombo.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(ProfileCombo.ItemsSource);
        view?.Refresh();
        ProfileCombo.SelectedItem = _viewModel?.SelectedConnection;
    }

    private void OnCommitFormBindings(object sender, RoutedEventArgs e)
    {
        ConnectionNameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        HostBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        PortBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        TimeoutBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        UsernameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        PasswordPlainBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
