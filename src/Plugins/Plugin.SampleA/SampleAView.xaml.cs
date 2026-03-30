using System.Windows.Controls;

namespace Plugin.SampleA;

public partial class SampleAView : UserControl
{
    public SampleAView()
    {
        InitializeComponent();
        DataContext = new SampleAViewModel();
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        OnDragEnter(sender, e);
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (paths != null && paths.Length > 0)
            {
                if (DataContext is SampleAViewModel vm)
                {
                    vm.HandleDroppedPathsAsync(paths);
                }
            }
        }
    }
}
