using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.InputRecorder;

public partial class InputRecorderView : UserControl
{
    private readonly InputRecorderViewModel _viewModel;

    public InputRecorderView(IPluginContext? context)
    {
        InitializeComponent();
        _viewModel = new InputRecorderViewModel(context);
        DataContext = _viewModel;
    }
}
