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
                    _context?.ReportError($"長図のキャプチャに失敗しました: {ex.Message}");
                    _hookService.NotifyLongScreenshotCaptureEnded();
                }
            });
        };
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
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            _context?.ReportProgress(msg, progress, ind));
                    }).ConfigureAwait(false);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        pending.ExtraPath = path;
                        _context?.ReportSuccess($"長図のキャプチャが完了しました: {path}");
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        _context?.ReportError($"長図のキャプチャに失敗しました: {ex.Message}"));
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

        var detail = "録画中… Escで終了・復元、F9で画面キャプチャ、F10で長図キャプチャ";
        _context?.ReportProgress("録画を開始しました。" + detail, 0, true);

        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;
        }

        _hookService.StartRecording();
        if (!_hookService.IsRecording)
        {
            IsRecording = false;
            const string hookErr = "フックの開始に失敗しました（管理者権限やセキュリティソフトを確認してください）";
            _context?.ReportError(hookErr);
            DesktopCornerToast.Show("マクロ録画", hookErr, DesktopCornerToastKind.Error, DesktopCornerToast.EndDisplayDuration);
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                System.Windows.Application.Current.MainWindow.Activate();
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

        var msg = $"録画を終了しました。イベント数: {Events.Count}";
        _context?.ReportSuccess(msg);
        DesktopCornerToast.Show("マクロ録画", $"終了しました。イベント数: {Events.Count}", DesktopCornerToastKind.Info, DesktopCornerToast.EndDisplayDuration);
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task ReplayAsync()
    {
        IsReplaying = true;
        const string replayStartMsg = "リプレイを開始しました。Escで中断できます。3秒後に実行します…";
        _context?.ReportProgress(replayStartMsg, 0, true);
        DesktopCornerToast.Show(
            "マクロリプレイ",
            "開始しました。Escで中断できます。3秒後に実行します。",
            DesktopCornerToastKind.Info,
            DesktopCornerToast.StartDisplayDuration);

        if (System.Windows.Application.Current?.MainWindow != null)
            System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;

        using var cts = new CancellationTokenSource();
        void HandleReplayEscapePressed() => cts.Cancel();
        _hookService.OnReplayEscapePressed += HandleReplayEscapePressed;

        try
        {
            await Task.Delay(3000, cts.Token);
            const string replayRunningMsg = "リプレイ実行中…（Escで中断）";
            _context?.ReportProgress(replayRunningMsg, 0, true);
            await _hookService.ReplayAsync(Events, cts.Token);
            const string replayDoneMsg = "リプレイを終了しました。正常に完了しました。";
            _context?.ReportSuccess(replayDoneMsg);
            DesktopCornerToast.Show("マクロリプレイ", "終了しました。正常に完了しました。", DesktopCornerToastKind.Info, DesktopCornerToast.EndDisplayDuration);
        }
        catch (OperationCanceledException)
        {
            const string replayCancelMsg = "リプレイを終了しました。Escにより中断されました。";
            _context?.ReportProgress(replayCancelMsg, 100, false);
            DesktopCornerToast.Show("マクロリプレイ", "終了しました。Escにより中断されました。", DesktopCornerToastKind.Warning, DesktopCornerToast.EndDisplayDuration);
        }
        catch (Exception ex)
        {
            _context?.ReportError($"リプレイ中にエラーが発生しました: {ex.Message}");
            DesktopCornerToast.Show("マクロリプレイ", $"終了しました。エラー: {ex.Message}", DesktopCornerToastKind.Error, DesktopCornerToast.EndDisplayDuration);
        }
        finally
        {
            _hookService.OnReplayEscapePressed -= HandleReplayEscapePressed;
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                System.Windows.Application.Current.MainWindow.Activate();
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

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = JsonSerializer.Serialize(Events, JsonOptions);
                await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(true);
                _context?.ReportSuccess("スクリプトの保存が完了しました。");
            }
            catch (Exception ex)
            {
                _context?.ReportError($"スクリプトの保存に失敗しました: {ex.Message}");
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

                    var msg = $"読み込みが完了しました（イベント数: {Events.Count}）";
                    _context?.ReportSuccess(msg);

                    ReplayCommand.NotifyCanExecuteChanged();
                    SaveCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                _context?.ReportError($"読み込みエラー: {ex.Message}");
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
