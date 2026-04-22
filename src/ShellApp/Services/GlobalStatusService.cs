using CommunityToolkit.Mvvm.ComponentModel;
using Plugin.Abstractions;
using System.Windows;
using System.Windows.Media;
using System.Text.Json;

namespace ShellApp.Services;

public partial class GlobalStatusService : ObservableObject, IPluginContext
{
    private enum StatusLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Debug
    }

    private StatusLevel _currentLevel = StatusLevel.Info;
    private readonly AppConfigService _appConfig;

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

    [ObservableProperty]
    private bool _isSuccessState;

    [ObservableProperty]
    private bool _isErrorState;

    public GlobalStatusService(AppConfigService appConfig)
    {
        _appConfig = appConfig;
        ApplyLevelStyle(StatusLevel.Info, "準備完了", includePrefix: false);
    }

    public string? GetPluginSetting(string pluginId)
    {
        if (_appConfig.Config.PluginSettings.TryGetValue(pluginId, out var element))
        {
            return element.GetRawText();
        }
        return null;
    }

    public void SavePluginSetting(string pluginId, string json)
    {
        _appConfig.SetPluginSetting(pluginId, JsonDocument.Parse(json).RootElement);
    }

    /// <summary>テーマ切り替え後に呼ぶ。現在レベルの色を再適用する。</summary>
    public void RefreshTextBrushAfterThemeChange()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            TextColor = ResolveLevelBrush(_currentLevel);
        });
    }

    private static string GetLevelPrefix(StatusLevel level) => level switch
    {
        StatusLevel.Info => "INFO",
        StatusLevel.Success => "SUCCESS",
        StatusLevel.Warning => "WARNING",
        StatusLevel.Error => "ERROR",
        StatusLevel.Debug => "DEBUG",
        _ => "INFO"
    };

    private static Color FromHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);

    private Brush ResolveLevelBrush(StatusLevel level)
    {
        return level switch
        {
            StatusLevel.Info => new SolidColorBrush(FromHex("#9AA0A6")),
            StatusLevel.Success => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
            StatusLevel.Warning => new SolidColorBrush(FromHex("#F4B400")),
            StatusLevel.Error => new SolidColorBrush(FromHex("#DC3545")),
            StatusLevel.Debug => new SolidColorBrush(FromHex("#6C8AA3")),
            _ => new SolidColorBrush(FromHex("#9AA0A6"))
        };
    }

    private void ApplyLevelStyle(
        StatusLevel level,
        string message,
        bool includePrefix = true,
        bool showProgress = false,
        double progressValue = 0,
        bool isIndeterminate = false)
    {
        _currentLevel = level;
        Message = includePrefix ? $"[{GetLevelPrefix(level)}] {message}" : message;
        ShowProgress = showProgress;
        ProgressValue = progressValue;
        IsIndeterminate = isIndeterminate;
        TextColor = ResolveLevelBrush(level);
        IsSuccessState = level == StatusLevel.Success;
        IsErrorState = level == StatusLevel.Error;
    }

    public void ReportProgress(string message, double percentage = 0, bool isIndeterminate = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // メッセージが空でない場合のみメッセージを更新する
            if (!string.IsNullOrWhiteSpace(message))
            {
                Message = $"[{GetLevelPrefix(StatusLevel.Info)}] {message}";
                _currentLevel = StatusLevel.Info;
                TextColor = ResolveLevelBrush(_currentLevel);
                IsSuccessState = false;
                IsErrorState = false;
            }

            // 100% かつ不確定でない場合はプログレスを非表示にする
            ShowProgress = isIndeterminate || percentage < 100;
            ProgressValue = percentage;
            IsIndeterminate = isIndeterminate;
        });
    }

    /// <summary>
    /// INFO の文言のみ更新する。進行中の <see cref="ReportProgress"/> によるプログレス表示は維持する。
    /// （DB エクスポート等で <see cref="IProgress{T}"/> から逐次ログするとき、<see cref="ApplyLevelStyle"/> 経由だと ShowProgress が落ちるため）
    /// </summary>
    public void ReportInfo(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _currentLevel = StatusLevel.Info;
            Message = $"[{GetLevelPrefix(StatusLevel.Info)}] {message}";
            TextColor = ResolveLevelBrush(_currentLevel);
            IsSuccessState = false;
            IsErrorState = false;
        });
    }

    public void ReportWarning(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyLevelStyle(StatusLevel.Warning, message, includePrefix: true);
        });
    }

    public void ReportSuccess(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyLevelStyle(StatusLevel.Success, message, includePrefix: true);
        });
    }

    public void ReportError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyLevelStyle(StatusLevel.Error, message, includePrefix: true);
        });
    }

    public void ReportDebug(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _currentLevel = StatusLevel.Debug;
            Message = $"[{GetLevelPrefix(StatusLevel.Debug)}] {message}";
            TextColor = ResolveLevelBrush(_currentLevel);
            IsSuccessState = false;
            IsErrorState = false;
        });
    }

    public void ClearStatus()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyLevelStyle(StatusLevel.Info, "準備完了", includePrefix: false);
        });
    }
}
