using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Abstractions;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;

namespace Plugin.PostgreCompare.ViewModels;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success
}

/// <summary>
/// プラグイン固有の保存設定モデル。
/// </summary>
public class PostgreCompareConfig
{
    public List<DatabaseConnection> Connections { get; set; } = new();
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private string _appAuthor = "wangys";

    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private int _progressValue;

    /// <summary>インポート・エクスポートタブ等で、シェル下部と同型の進捗表示にバインドする。</summary>
    [ObservableProperty]
    private bool _showProgress;

    [ObservableProperty]
    private bool _progressIndeterminate;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private DbConfigViewModel _dbConfigViewModel;

    [ObservableProperty]
    private ImportExportViewModel _importExportViewModel;

    [ObservableProperty]
    private CompareViewModel _compareViewModel;

    private readonly IPluginContext? _context;
    private const string PluginId = "postgreCompare";

    /// <summary>
    /// 設定読み込み中は <see cref="DbConfigViewModel"/> の CollectionChanged による自動保存を抑止する。
    /// </summary>
    public bool IsBulkUpdatingConnections { get; private set; }

    public MainViewModel(IPluginContext? context)
    {
        _context = context;

        DbConfigViewModel = new DbConfigViewModel(this);
        ImportExportViewModel = new ImportExportViewModel(this);
        CompareViewModel = new CompareViewModel(this);

        DbConfigViewModel.Connections = Connections;
        ImportExportViewModel.Connections = Connections;
        CompareViewModel.Connections = Connections;

        LoadConnections();
    }

    private void LoadConnections()
    {
        try
        {
            var json = _context?.GetPluginSetting(PluginId);
            if (!string.IsNullOrEmpty(json))
            {
                var config = JsonSerializer.Deserialize<PostgreCompareConfig>(json);
                if (config?.Connections != null)
                {
                    IsBulkUpdatingConnections = true;
                    try
                    {
                        Connections.Clear();
                        foreach (var conn in config.Connections)
                        {
                            Connections.Add(conn);
                        }
                    }
                    finally
                    {
                        IsBulkUpdatingConnections = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ReportError($"設定の読み込みに失敗しました: {ex.Message}");
        }
    }

    public void SaveConnections()
    {
        try
        {
            var config = new PostgreCompareConfig
            {
                Connections = Connections.ToList()
            };
            var json = JsonSerializer.Serialize(config);
            _context?.SavePluginSetting(PluginId, json);
            ReportSuccess("接続設定を保存しました。");
        }
        catch (Exception ex)
        {
            ReportError($"設定の保存に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearStatus()
    {
        _context?.ClearStatus();
    }

    public void ReportInfo(string message) => PostToUi(() => _context?.ReportInfo(message));
    public void ReportWarning(string message) => PostToUi(() => _context?.ReportWarning(message));
    public void ReportError(string message) => PostToUi(() => _context?.ReportError(message));
    public void ReportSuccess(string message) => PostToUi(() => _context?.ReportSuccess(message));

    public void ReportProgress(string message, double percentage = 0, bool isIndeterminate = false)
    {
        PostToUi(() =>
        {
            ProgressValue = (int)percentage;
            ProgressIndeterminate = isIndeterminate;
            ShowProgress = isIndeterminate || percentage < 100;
            IsProcessing = isIndeterminate || (percentage > 0 && percentage < 100);
            _context?.ReportProgress(message, percentage, isIndeterminate);
        });
    }

    public void AppendLog(string message, LogLevel level = LogLevel.Info)
    {
        switch (level)
        {
            case LogLevel.Error:
                ReportError(message);
                break;
            case LogLevel.Warning:
                ReportWarning(message);
                break;
            case LogLevel.Success:
                ReportSuccess(message);
                break;
            default:
                ReportInfo(message);
                break;
        }
    }

    public void AppendLog(string message) => AppendLog(message, LogLevel.Info);

    private static void PostToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }
}

