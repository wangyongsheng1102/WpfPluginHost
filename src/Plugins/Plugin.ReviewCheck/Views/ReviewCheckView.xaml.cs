using Plugin.Abstractions;
using Plugin.ReviewCheck.ViewModels;

namespace Plugin.ReviewCheck.Views;

public partial class ReviewCheckView : System.Windows.Controls.UserControl
{
    public ReviewCheckView(IPluginContext? context)
    {
        InitializeComponent();
        DataContext = new ReviewCheckViewModel(context);
    }
}
