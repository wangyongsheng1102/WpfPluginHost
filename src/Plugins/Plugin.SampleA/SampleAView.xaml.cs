using System.Windows;
using System.Windows.Controls;

namespace Plugin.SampleA;

public partial class SampleAView : UserControl
{
    public SampleAView(Plugin.Abstractions.IPluginContext? context = null)
    {
        InitializeComponent();
        DataContext = new SampleAViewModel(context);
    }

    private void OnDropZonePreviewDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not SampleAViewModel vm)
            return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            vm.IsDragOverDropZone = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            vm.IsDragOverDropZone = false;
        }
    }

    private void OnDropZonePreviewDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is SampleAViewModel vm)
            vm.IsDragOverDropZone = false;
    }

    private void OnDropZonePreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SampleAViewModel vm)
            return;
        vm.IsDragOverDropZone = false;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (paths == null || paths.Length == 0)
            return;
        _ = vm.HandleDroppedPathsAsync(paths);
        e.Handled = true;
    }
}
