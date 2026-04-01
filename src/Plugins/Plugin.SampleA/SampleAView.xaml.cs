using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Plugin.SampleA;

public partial class SampleAView : UserControl
{
    private SampleAViewModel? _viewModel;

    public SampleAView(Plugin.Abstractions.IPluginContext? context = null)
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataContext = new SampleAViewModel(context);
        AttachViewModel(DataContext as SampleAViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SampleAViewModel oldVm)
        {
            oldVm.PendingFiles.CollectionChanged -= OnPendingFilesCollectionChanged;
            foreach (var item in oldVm.PendingFiles)
            {
                item.PropertyChanged -= OnPendingFilePropertyChanged;
            }
        }

        AttachViewModel(e.NewValue as SampleAViewModel);
    }

    private void AttachViewModel(SampleAViewModel? vm)
    {
        _viewModel = vm;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PendingFiles.CollectionChanged += OnPendingFilesCollectionChanged;
        foreach (var item in _viewModel.PendingFiles)
        {
            item.PropertyChanged += OnPendingFilePropertyChanged;
        }

        ScheduleAutoSizeColumns();
    }

    private void OnPendingFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ExcelFileItem item in e.OldItems)
            {
                item.PropertyChanged -= OnPendingFilePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ExcelFileItem item in e.NewItems)
            {
                item.PropertyChanged += OnPendingFilePropertyChanged;
            }
        }

        ScheduleAutoSizeColumns();
    }

    private void OnPendingFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExcelFileItem.Status) or nameof(ExcelFileItem.StatusDetail) or nameof(ExcelFileItem.FilePath))
        {
            ScheduleAutoSizeColumns();
        }
    }

    private void ScheduleAutoSizeColumns()
    {
        Dispatcher.InvokeAsync(AutoSizePendingFilesColumns, DispatcherPriority.Background);
    }

    private void AutoSizePendingFilesColumns()
    {
        if (PendingFilesDataGrid.Columns.Count == 0)
        {
            return;
        }

        PendingFilesDataGrid.UpdateLayout();
        for (var i = 0; i < PendingFilesDataGrid.Columns.Count; i++)
        {
            var col = PendingFilesDataGrid.Columns[i];
            col.Width = i == PendingFilesDataGrid.Columns.Count - 1
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(1, DataGridLengthUnitType.Auto);
        }
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
