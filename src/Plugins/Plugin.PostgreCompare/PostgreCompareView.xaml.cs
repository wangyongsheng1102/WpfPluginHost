using System.Windows.Controls;
using Plugin.Abstractions;
using Plugin.PostgreCompare.ViewModels;

namespace Plugin.PostgreCompare;

public partial class PostgreCompareView : UserControl
{
    public PostgreCompareView(IPluginContext? context)
    {
        InitializeComponent();
        DataContext = new MainViewModel(context);
    }
}

