using Plugin.Abstractions;
using Plugin.PixelCompare.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Plugin.PixelCompare;

public partial class PixelCompareView : UserControl
{
    private DateTime _lastPreviewClickAt = DateTime.MinValue;
    private PixelCompareViewModel? _viewModel;

    public PixelCompareView(IPluginContext? context)
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataContext = new PixelCompareViewModel(context);
        AttachViewModel(DataContext as PixelCompareViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PixelCompareViewModel oldVm)
        {
            oldVm.CompareCompleted -= OnCompareCompleted;
        }

        AttachViewModel(e.NewValue as PixelCompareViewModel);
    }

    private void AttachViewModel(PixelCompareViewModel? vm)
    {
        _viewModel = vm;
        if (_viewModel is not null)
        {
            _viewModel.CompareCompleted += OnCompareCompleted;
        }
    }

    private async void OnCompareCompleted(object? sender, EventArgs e)
    {
        await Dispatcher.InvokeAsync(AutoSizeCompareListColumns, DispatcherPriority.Background);
    }

    private void AutoSizeCompareListColumns()
    {
        if (CompareListDataGrid.Columns.Count == 0)
        {
            return;
        }

        CompareListDataGrid.UpdateLayout();
        foreach (var column in CompareListDataGrid.Columns)
        {
            column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
        }
    }

    private void OnPreviewImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            // DataGrid やスクロールの環境で ClickCount が不安定な場合の保険。
            var now = DateTime.UtcNow;
            if ((now - _lastPreviewClickAt).TotalMilliseconds > 350)
            {
                _lastPreviewClickAt = now;
                return;
            }
        }

        _lastPreviewClickAt = DateTime.UtcNow;

        if (DataContext is not PixelCompareViewModel vm)
        {
            return;
        }

        var imagePath = vm.PreviewImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = imagePath,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // 既定アプリ起動失敗時は UI を止めない。
        }
    }
}
