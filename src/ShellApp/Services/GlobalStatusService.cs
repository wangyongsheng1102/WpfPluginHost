using CommunityToolkit.Mvvm.ComponentModel;
using Plugin.Abstractions;
using System.Windows;
using System.Windows.Media;

namespace ShellApp.Services;

public partial class GlobalStatusService : ObservableObject, IPluginContext
{
    [ObservableProperty]
    private string _message = "準備完了";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private bool _isIndeterminate = false;

    [ObservableProperty]
    private bool _showProgress = false;

    [ObservableProperty]
    private Brush _statusColor = new SolidColorBrush(Colors.Transparent);
    
    [ObservableProperty]
    private Brush _textColor = new SolidColorBrush(Colors.White);

    public void ReportProgress(string message, double percentage = 0, bool isIndeterminate = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = message;
            ProgressValue = percentage;
            IsIndeterminate = isIndeterminate;
            ShowProgress = true;
            TextColor = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        });
    }

    public void ReportSuccess(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "✅ " + message;
            ShowProgress = false;
            TextColor = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Success Green
        });
    }

    public void ReportError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "❌ " + message;
            ShowProgress = false;
            TextColor = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Error Red
        });
    }

    public void ClearStatus()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "準備完了";
            ShowProgress = false;
            TextColor = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        });
    }
}
