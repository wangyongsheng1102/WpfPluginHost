using CommunityToolkit.Mvvm.ComponentModel;
using Plugin.Abstractions;
using System.Windows;
using System.Windows.Media;

namespace ShellApp.Services;

public partial class GlobalStatusService : ObservableObject, IPluginContext
{
    private enum StatusTextTone
    {
        Neutral,
        Success,
        Error
    }

    private StatusTextTone _textTone = StatusTextTone.Neutral;

    [ObservableProperty]
    private string _message = "準備完了";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private bool _isIndeterminate = false;

    [ObservableProperty]
    private bool _showProgress = false;

    [ObservableProperty]
    private Brush _textColor = Brushes.Transparent;

    public GlobalStatusService()
    {
        ApplyNeutralTextBrushFromTheme();
    }

    /// <summary>テーマ切り替え後に呼ぶ。中性メッセージの色は現在の <c>PrimaryTextBrush</c> に合わせ直す。</summary>
    public void RefreshTextBrushAfterThemeChange()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_textTone == StatusTextTone.Neutral)
                ApplyNeutralTextBrushFromTheme();
        });
    }

    private void ApplyNeutralTextBrushFromTheme()
    {
        _textTone = StatusTextTone.Neutral;
        if (Application.Current?.TryFindResource("PrimaryTextBrush") is Brush brush)
            TextColor = brush;
        else
            TextColor = new SolidColorBrush(Colors.Gray);
    }

    public void ReportProgress(string message, double percentage = 0, bool isIndeterminate = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = message;
            ProgressValue = percentage;
            IsIndeterminate = isIndeterminate;
            ShowProgress = true;
            ApplyNeutralTextBrushFromTheme();
        });
    }

    public void ReportSuccess(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "✅ " + message;
            ShowProgress = false;
            _textTone = StatusTextTone.Success;
            TextColor = new SolidColorBrush(Color.FromRgb(40, 167, 69));
        });
    }

    public void ReportError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "❌ " + message;
            ShowProgress = false;
            _textTone = StatusTextTone.Error;
            TextColor = new SolidColorBrush(Color.FromRgb(220, 53, 69));
        });
    }

    public void ClearStatus()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = "準備完了";
            ShowProgress = false;
            ApplyNeutralTextBrushFromTheme();
        });
    }
}
