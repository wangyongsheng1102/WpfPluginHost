using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Abstractions;
using Plugin.InputRecorder.Models;
using Plugin.InputRecorder.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.InputRecorder;

public partial class InputRecorderViewModel : ObservableObject
{
    private readonly IPluginContext? _context;
    private readonly InputHookService _hookService;

    [ObservableProperty]
    private string _statusMessage = "準備完了";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecord))]
    [NotifyPropertyChangedFor(nameof(CanReplay))]
    [NotifyPropertyChangedFor(nameof(CanLoad))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecord))]
    [NotifyPropertyChangedFor(nameof(CanReplay))]
    [NotifyPropertyChangedFor(nameof(CanLoad))]
    private bool _isReplaying;

    [ObservableProperty]
    private string? _chromeExecutablePath;

    partial void OnChromeExecutablePathChanged(string? value)
    {
        SaveSettings();
    }

    public ObservableCollection<InputEvent> Events { get; } = new();

    public bool CanRecord => !IsRecording && !IsReplaying;
    public bool CanReplay => !IsRecording && !IsReplaying && Events.Count > 0;
    public bool CanLoad => !IsRecording && !IsReplaying;
    public bool CanSave => !IsRecording && Events.Count > 0;

    public InputRecorderViewModel(IPluginContext? context)
    {
        _context = context;
        LoadSettings();
        
        _hookService = new InputHookService();
        _hookService.OnEscapePressed += () =>
        {
            if (IsRecording && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => StopRecording());
            }
        };
        _hookService.OnScreenshotCaptured += (path) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _context?.ReportSuccess($"スクリーンショットを保存しました: {path}");
            });
        };

        _hookService.OnF10Pressed += () =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(async () =>
            {
                await HandleF10CaptureAsync();
            });
        };
    }

    private bool _isCapturingPuppeteer = false;
    private string? _lastValidUrl;

    private async Task HandleF10CaptureAsync()
    {
        if (_isCapturingPuppeteer) return;

        string? url = null;
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                url = System.Windows.Clipboard.GetText()?.Trim();
            }
        }
        catch { /* クリップボードの例外を無視する */ }

        if (string.IsNullOrWhiteSpace(url) || (!url.StartsWith("http://") && !url.StartsWith("https://")))
        {
            if (!string.IsNullOrWhiteSpace(_lastValidUrl))
            {
                url = _lastValidUrl; // Use the previously used valid URL
            }
            else
            {
                _context?.ReportError("クリップボードに有効なURLが見つかりません。対象ブラウザでURLをコピー（Ctrl+C）してからF10を押してください。");
                return;
            }
        }
        else
        {
            _lastValidUrl = url; // Remember correctly formatted URL
        }

        _isCapturingPuppeteer = true;
        
        var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputRecord");
        if (!System.IO.Directory.Exists(folder))
            System.IO.Directory.CreateDirectory(folder);

        var fileName = $"FullPage_{Guid.NewGuid().ToString("N")[..8]}.png";
        var path = System.IO.Path.Combine(folder, fileName);

        var isCustomChrome = !string.IsNullOrWhiteSpace(ChromeExecutablePath) && File.Exists(ChromeExecutablePath);
        if (isCustomChrome)
            _context?.ReportProgress("指定されたChromeを利用してフル画面長図をキャプチャ中...", 0, true);
        else
            _context?.ReportProgress("Chromiumを自動利用してフル画面長図をキャプチャ中... (初回はダウンロードで数分かかります)", 0, true);

        await Task.Run(async () =>
        {
            try
            {
                var puppeteer = new PuppeteerService();
                await puppeteer.CaptureFullPageAsync(url, path, ChromeExecutablePath);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _context?.ReportSuccess($"長図キャプチャ完了: {path}");
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _context?.ReportError($"長図キャプチャ失敗: {ex.Message}");
                });
            }
            finally
            {
                _isCapturingPuppeteer = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanRecord))]
    private void StartRecording()
    {
        Events.Clear();
        IsRecording = true;
        
        var msg = "録画中... (Escで終了・復元、F9で画面キャプチャ、F10でフル画面長図キャプチャ)";
        StatusMessage = "● " + msg;
        _context?.ReportProgress(msg, 0, true);
        
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;
        }

        _hookService.StartRecording();
    }

    [RelayCommand]
    private void StopRecording()
    {
        if (IsRecording)
        {
            _hookService.StopRecording();
            IsRecording = false;
            
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                System.Windows.Application.Current.MainWindow.Activate();
            }
            
            var recorded = _hookService.GetRecordedEvents();
            Events.Clear();
            foreach (var ev in recorded)
            {
                Events.Add(ev);
            }
            
            SaveCommand.NotifyCanExecuteChanged();
            ReplayCommand.NotifyCanExecuteChanged();
            
            var msg = $"録画完了 (イベント数: {Events.Count})";
            StatusMessage = msg;
            _context?.ReportSuccess(msg);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task ReplayAsync()
    {
        IsReplaying = true;
        StatusMessage = "▶ リプレイ中...";
        _context?.ReportProgress("リプレイ実行中...", 0, true);
        
        using var cts = new CancellationTokenSource();
        // ユーザーがマウス・キーボードから手を離すための猶予（1秒）を与える
        await Task.Delay(1000, cts.Token);
        
        try
        {
            await _hookService.ReplayAsync(Events, cts.Token);
            StatusMessage = "リプレイ完了";
            _context?.ReportSuccess("リプレイが完了しました。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
            _context?.ReportError($"リプレイエラー: {ex.Message}");
        }
        finally
        {
            IsReplaying = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };
        
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(Events, options);
                await File.WriteAllTextAsync(dialog.FileName, json);
                StatusMessage = "保存完了";
                _context?.ReportSuccess("スクリプトの保存が完了しました。");
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存エラー: {ex.Message}";
                _context?.ReportError($"保存エラー: {ex.Message}");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };
        
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await File.ReadAllTextAsync(dialog.FileName);
                var loaded = JsonSerializer.Deserialize<ObservableCollection<InputEvent>>(json);
                if (loaded != null)
                {
                    Events.Clear();
                    foreach (var ev in loaded)
                    {
                        Events.Add(ev);
                    }
                    _hookService.LoadEvents(Events);
                    
                    var msg = $"読み込み完了 (イベント数: {Events.Count})";
                    StatusMessage = msg;
                    _context?.ReportSuccess(msg);
                    
                    // コマンドの状態を再評価する
                    ReplayCommand.NotifyCanExecuteChanged();
                    SaveCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"読込エラー: {ex.Message}";
                _context?.ReportError($"読込エラー: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void BrowseChromePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Chrome.exe の場所を選択してください"
        };
        
        if (dialog.ShowDialog() == true)
        {
            ChromeExecutablePath = dialog.FileName;
        }
    }

    private class PluginSettings
    {
        public string? ChromePath { get; set; }
    }

    private void LoadSettings()
    {
        var json = _context?.GetPluginSetting("inputRecorder");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<PluginSettings>(json);
                if (settings != null)
                {
                    _chromeExecutablePath = settings.ChromePath;
                }
            }
            catch { }
        }
    }

    private void SaveSettings()
    {
        var settings = new PluginSettings { ChromePath = ChromeExecutablePath };
        try
        {
            var json = JsonSerializer.Serialize(settings);
            _context?.SavePluginSetting("inputRecorder", json);
        }
        catch { }
    }
}
