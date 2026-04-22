using System.Windows;
using System.Windows.Controls;

namespace Plugin.ExcelFormatter;

public partial class ExcelFormatterView : UserControl
{
    public ExcelFormatterView(Plugin.Abstractions.IPluginContext? context = null)
    {
        InitializeComponent();
        DataContext = new ExcelFormatterViewModel(context);
    }

    private void OnDropZonePreviewDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not ExcelFormatterViewModel vm) return;
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
        if (DataContext is ExcelFormatterViewModel vm)
            vm.IsDragOverDropZone = false;
    }

    private void OnDropZonePreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ExcelFormatterViewModel vm) return;
        vm.IsDragOverDropZone = false;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (paths == null || paths.Length == 0) return;
        _ = vm.HandleDroppedPathsAsync(paths);
        e.Handled = true;
    }
}
