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
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WindowState = System.Windows.WindowState;

namespace Plugin.InputRecorder;

public partial class InputRecorderViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext? _context;
    private readonly InputHookService _hookService;
    private readonly Dispatcher? _dispatcher;
    private bool _disposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isReplaying;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _eventCount;

    public ObservableCollection<InputEventDisplay> DisplayEvents { get; } = new();

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
        _dispatcher = WpfApplication.Current?.Dispatcher;

        _hookService = new InputHookService();
        _hookService.OnEscapePressed += OnEscapePressed;
        _hookService.OnScreenshotCaptured += OnScreenshotCaptured;
        _hookService.OnLongScreenshotRecordingRequested += OnLongScreenshotRequested;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private void BeginOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    private void OnEscapePressed()
    {
        if (IsRecording)
            RunOnUi(StopRecording);
    }

    private void OnScreenshotCaptured(string path)
    {
        BeginOnUi(() => _context?.ReportSuccess($"スクリーンショットを保存しました: {path}"));
    }

    private void OnLongScreenshotRequested(InputEvent pending)
    {
        if (_dispatcher is null) return;
        _ = _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await HandleLongScreenshotRecordingAsync(pending).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context?.ReportError($"長図のキャプチャに失敗しました: {ex.Message}");
                _hookService.NotifyLongScreenshotCaptureEnded();
            }
        });
    }

    private async Task HandleLongScreenshotRecordingAsync(InputEvent pending)
    {
        var path = _hookService.GenerateLongScreenshotPathForRecording();
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
                        BeginOnUi(() => _context?.ReportProgress(msg, progress, ind));
                    }).ConfigureAwait(false);

                    BeginOnUi(() =>
                    {
                        pending.ExtraPath = path;
                        _context?.ReportSuccess($"長図のキャプチャが完了しました: {path}");
                    });
                }
                catch (Exception ex)
                {
                    BeginOnUi(() => _context?.ReportError($"長図のキャプチャに失敗しました: {ex.Message}"));
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

    private bool CanStartRecording() => !IsRecording && !IsReplaying;
    private bool CanStopRecording() => IsRecording;
    private bool CanReplay() => !IsRecording && !IsReplaying && EventCount > 0;
    private bool CanSave() => !IsRecording && !IsReplaying && EventCount > 0;
    private bool CanLoad() => !IsRecording && !IsReplaying;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private void StartRecording()
    {
        DisplayEvents.Clear();
        EventCount = 0;
        IsRecording = true;

        _context?.ReportProgress("録画を開始しました。Escで終了・復元、F9で画面キャプチャ、F10で長図キャプチャ", 0, true);

        var mainWindow = WpfApplication.Current?.MainWindow;
        if (mainWindow != null)
            mainWindow.WindowState = WindowState.Minimized;

        _hookService.StartRecording();
        if (!_hookService.IsRecording)
        {
            IsRecording = false;
            const string hookErr = "フックの開始に失敗しました（管理者権限やセキュリティソフトを確認してください）";
            _context?.ReportError(hookErr);
            DesktopCornerToast.Show("マクロ録画", hookErr, DesktopCornerToastKind.Error, DesktopCornerToast.EndDisplayDuration);
            if (mainWindow != null)
            {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }
        else
        {
            DesktopCornerToast.Show(
                "マクロ録画",
                "開始しました。Escで終了・復元、F9で画面キャプチャ、F10で長図キャプチャ。",
                DesktopCornerToastKind.Info,
                DesktopCornerToast.StartDisplayDuration);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private void StopRecording()
    {
        if (!IsRecording) return;

        _hookService.StopRecording();
        IsRecording = false;

        var mainWindow = WpfApplication.Current?.MainWindow;
        if (mainWindow != null)
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        PopulateDisplayEvents(_hookService.GetRecordedEvents());

        StatusText = $"録画を終了しました。イベント数: {EventCount}";
        _context?.ReportSuccess(StatusText);
        DesktopCornerToast.Show("マクロ録画", $"終了しました。イベント数: {EventCount}", DesktopCornerToastKind.Info, DesktopCornerToast.EndDisplayDuration);
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task ReplayAsync()
    {
        IsReplaying = true;
        StatusText = "リプレイを開始しました。3秒後に実行します…";
        _context?.ReportProgress(StatusText, 0, true);
        DesktopCornerToast.Show("マクロリプレイ", "開始しました。Escで中断できます。3秒後に実行します。", DesktopCornerToastKind.Info, DesktopCornerToast.StartDisplayDuration);

        var mainWindow = WpfApplication.Current?.MainWindow;
        if (mainWindow != null)
            mainWindow.WindowState = WindowState.Minimized;

        using var cts = new CancellationTokenSource();
        void HandleReplayEscapePressed() => cts.Cancel();
        _hookService.OnReplayEscapePressed += HandleReplayEscapePressed;

        try
        {
            await Task.Delay(3000, cts.Token);
            StatusText = "リプレイ実行中…（Escで中断）";
            _context?.ReportProgress(StatusText, 0, true);
            await _hookService.ReplayAsync(_hookService.GetRecordedEvents(), cts.Token);
            StatusText = "リプレイを終了しました。正常に完了しました。";
            _context?.ReportSuccess(StatusText);
            DesktopCornerToast.Show("マクロリプレイ", "終了しました。正常に完了しました。", DesktopCornerToastKind.Info, DesktopCornerToast.EndDisplayDuration);
        }
        catch (OperationCanceledException)
        {
            StatusText = "リプレイを終了しました。Escにより中断されました。";
            _context?.ReportProgress(StatusText, 100, false);
            DesktopCornerToast.Show("マクロリプレイ", "終了しました。Escにより中断されました。", DesktopCornerToastKind.Warning, DesktopCornerToast.EndDisplayDuration);
        }
        catch (Exception ex)
        {
            StatusText = $"リプレイ中にエラーが発生しました: {ex.Message}";
            _context?.ReportError(StatusText);
            DesktopCornerToast.Show("マクロリプレイ", $"終了しました。エラー: {ex.Message}", DesktopCornerToastKind.Error, DesktopCornerToast.EndDisplayDuration);
        }
        finally
        {
            _hookService.OnReplayEscapePressed -= HandleReplayEscapePressed;
            if (mainWindow != null)
            {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }

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

        if (dialog.ShowDialog() != true) return;

        try
        {
            var events = _hookService.GetRecordedEvents();
            var json = JsonSerializer.Serialize(events, JsonOptions);
            await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(true);
            _context?.ReportSuccess("スクリプトの保存が完了しました。");
        }
        catch (Exception ex)
        {
            _context?.ReportError($"スクリプトの保存に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
            var loaded = JsonSerializer.Deserialize<List<InputEvent>>(json, JsonOptions);
            if (loaded != null)
            {
                _hookService.LoadEvents(loaded);
                PopulateDisplayEvents(loaded);
                _context?.ReportSuccess($"読み込みが完了しました（イベント数: {EventCount}）");
            }
        }
        catch (Exception ex)
        {
            _context?.ReportError($"読み込みエラー: {ex.Message}");
        }
    }

    private void PopulateDisplayEvents(IReadOnlyList<InputEvent> events)
    {
        DisplayEvents.Clear();
        foreach (var ev in events)
            DisplayEvents.Add(new InputEventDisplay(ev));
        EventCount = events.Count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hookService.OnEscapePressed -= OnEscapePressed;
        _hookService.OnScreenshotCaptured -= OnScreenshotCaptured;
        _hookService.OnLongScreenshotRecordingRequested -= OnLongScreenshotRequested;
        _hookService.Dispose();
    }
}
