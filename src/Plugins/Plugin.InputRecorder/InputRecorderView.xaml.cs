using Plugin.Abstractions;
using System.Windows;

namespace Plugin.InputRecorder;

public partial class InputRecorderView : System.Windows.Controls.UserControl
{
    private readonly InputRecorderViewModel _viewModel;

    public InputRecorderView(IPluginContext? context)
    {
        InitializeComponent();
        _viewModel = new InputRecorderViewModel(context);
        DataContext = _viewModel;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        _viewModel.Dispose();
    }
}
