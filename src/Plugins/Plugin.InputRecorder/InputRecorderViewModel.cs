using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Abstractions;
using Plugin.InputRecorder.Models;
using Plugin.InputRecorder.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.InputRecorder;

public partial class InputRecorderViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext? _context;
    private readonly InputHookService _hookService;
    private bool _disposed;

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

    public ObservableCollection<InputEvent> Events { get; } = new();

    public bool CanRecord => !IsRecording && !IsReplaying;
    public bool CanReplay => !IsRecording && !IsReplaying && Events.Count > 0;
    public bool CanLoad => !IsRecording && !IsReplaying;
    public bool CanSave => !IsRecording && !IsReplaying && Events.Count > 0;

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InputRecorderViewModel(IPluginContext? context)
    {
        _context = context;

        _hookService = new InputHookService();
        _hookService.OnEscapePressed += () =>
        {
            if (IsRecording && System.Windows.Application.Current != null)
                System.Windows.Application.Current.Dispatcher.Invoke(StopRecording);
        };

        _hookService.OnScreenshotCaptured += path =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                _context?.ReportSuccess($"スクリーンショットを保存しました: {path}"));
        };

        _hookService.OnLongScreenshotRecordingRequested += pending =>
        {
            if (System.Windows.Application.Current?.Dispatcher is null) return;
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await HandleLongScreenshotRecordingAsync(pending).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _context?.ReportError($"長図キャプチャ失敗: {ex.Message}");
                    _hookService.NotifyLongScreenshotCaptureEnded();
                }
            });
        };
    }

    private async Task HandleLongScreenshotRecordingAsync(InputEvent pending)
    {
        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputRecord");
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var fileName = $"FullPage_{Guid.NewGuid():N}.png";
        var path = Path.Combine(folder, fileName);

        _context?.ReportProgress("アクティブ画面の長図キャプチャを開始します...", 0, true);

        try
        {
            await Task.Run(async () =>
            {
                try
                {
                    var stitcher = new LongScreenshotService();
                    await stitcher.CaptureLongScreenshotAsync(path, (msg, progress, ind) =>
                    {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            _context?.ReportProgress(msg, progress, ind));
                    }).ConfigureAwait(false);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        pending.ExtraPath = path;
                        _context?.ReportSuccess($"長図キャプチャ完了: {path}");
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        _context?.ReportError($"長図キャプチャ失敗: {ex.Message}"));
                }
                finally
                {
                    _hookService.NotifyLongScreenshotCaptureEnded();
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            _hookService.NotifyLongScreenshotCaptureEnded();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecord))]
    private void StartRecording()
    {
        Events.Clear();
        IsRecording = true;

        var msg = "録画中... (Escで終了・復元、F9で画面キャプチャ、F10で長図キャプチャ)";
        StatusMessage = "● " + msg;
        _context?.ReportProgress(msg, 0, true);

        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;
        }

        _hookService.StartRecording();
        if (!_hookService.IsRecording)
        {
            IsRecording = false;
            StatusMessage = "フックの開始に失敗しました（管理者権限やセキュリティソフトを確認してください）";
            _context?.ReportError(StatusMessage);
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                System.Windows.Application.Current.MainWindow.Activate();
            }
        }
    }

    [RelayCommand]
    private void StopRecording()
    {
        if (!IsRecording) return;

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
            Events.Add(ev);

        SaveCommand.NotifyCanExecuteChanged();
        ReplayCommand.NotifyCanExecuteChanged();

        var msg = $"録画完了 (イベント数: {Events.Count})";
        StatusMessage = msg;
        _context?.ReportSuccess(msg);
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task ReplayAsync()
    {
        IsReplaying = true;
        StatusMessage = "▶ リプレイ中...";
        _context?.ReportProgress("リプレイ実行中...", 0, true);

        using var cts = new CancellationTokenSource();
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
                var json = JsonSerializer.Serialize(Events, JsonOptions);
                await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(true);
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
                var json = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
                var loaded = JsonSerializer.Deserialize<List<InputEvent>>(json, JsonOptions);
                if (loaded != null)
                {
                    Events.Clear();
                    foreach (var ev in loaded)
                        Events.Add(ev);

                    _hookService.LoadEvents(Events);

                    var msg = $"読み込み完了 (イベント数: {Events.Count})";
                    StatusMessage = msg;
                    _context?.ReportSuccess(msg);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hookService.Dispose();
    }
}
