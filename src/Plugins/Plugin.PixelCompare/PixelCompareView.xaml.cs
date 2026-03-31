using Plugin.Abstractions;
using Plugin.PixelCompare.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;

namespace Plugin.PixelCompare;

public partial class PixelCompareView : UserControl
{
    private DateTime _lastPreviewClickAt = DateTime.MinValue;

    public PixelCompareView(IPluginContext? context)
    {
        InitializeComponent();
        DataContext = new PixelCompareViewModel(context);
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
