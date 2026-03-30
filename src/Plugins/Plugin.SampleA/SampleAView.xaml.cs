using System.Windows.Controls;

namespace Plugin.SampleA;

public partial class SampleAView : UserControl
{
    public SampleAView()
    {
        InitializeComponent();
        DataContext = new SampleAViewModel();
    }
}
