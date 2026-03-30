using System.Windows.Controls;

namespace Plugin.SampleB;

public partial class SampleBView : UserControl
{
    public SampleBView()
    {
        InitializeComponent();
        DataContext = new SampleBViewModel();
    }
}
