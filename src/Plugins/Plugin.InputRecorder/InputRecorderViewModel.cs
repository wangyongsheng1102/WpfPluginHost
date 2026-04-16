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

    public ObservableCollection<InputEvent> Events { get; } = new();

    public bool CanRecord => !IsRecording && !IsReplaying;
    public bool CanReplay => !IsRecording && !IsReplaying && Events.Count > 0;
    public bool CanLoad => !IsRecording && !IsReplaying;
    public bool CanSave => !IsRecording && Events.Count > 0;

    public InputRecorderViewModel(IPluginContext? context)
    {
        _context = context;
        _hookService = new InputHookService();
        _hookService.OnEscapePressed += () =>
        {
            if (IsRecording && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => StopRecording());
            }
        };
    }

    [RelayCommand(CanExecute = nameof(CanRecord))]
    private void StartRecording()
    {
        Events.Clear();
        IsRecording = true;
        StatusMessage = "● 録画中... (ESCキーで終了して復元します)";
        
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
            
            StatusMessage = $"録画完了 (イベント数: {Events.Count})";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task ReplayAsync()
    {
        IsReplaying = true;
        StatusMessage = "▶ リプレイ中...";
        
        using var cts = new CancellationTokenSource();
        // Give user 1 second to release mouse/keyboard
        await Task.Delay(1000, cts.Token);
        
        try
        {
            await _hookService.ReplayAsync(Events, cts.Token);
            StatusMessage = "リプレイ完了";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
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
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存エラー: {ex.Message}";
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
                    StatusMessage = $"読み込み完了 (イベント数: {Events.Count})";
                    
                    // Trigger Requery for commands
                    OnPropertyChanged(nameof(CanReplay));
                    OnPropertyChanged(nameof(CanSave));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"読込エラー: {ex.Message}";
            }
        }
    }
}
