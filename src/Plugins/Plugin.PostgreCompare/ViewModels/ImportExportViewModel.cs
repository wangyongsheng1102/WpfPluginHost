using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;
using System.Linq;

namespace Plugin.PostgreCompare.ViewModels;

public partial class ImportExportViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<TableInfo> _tables = new();

    public int SelectedTableCount => Tables.Count(t => t.IsSelected);

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFolderPath = string.Empty;

    [ObservableProperty]
    private string _importFolderPath = string.Empty;

    private readonly DatabaseService _databaseService = new();
    private readonly MainViewModel _mainViewModel;

    public ImportExportViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("接続を選択してください。", LogLevel.Error);
            return;
        }

        IsProcessing = true;
        _mainViewModel.AppendLog("テーブルリストを取得しています...");

        var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString());
        Tables.Clear();
        foreach (var table in tables)
        {
            table.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TableInfo.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedTableCount));
                }
            };
            Tables.Add(table);
        }

        OnPropertyChanged(nameof(SelectedTableCount));
        IsProcessing = false;
        _mainViewModel.AppendLog($"{Tables.Count} 個のテーブルを取得しました。", LogLevel.Success);
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedTableCount));
    }

    [RelayCommand]
    private void DeselectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedTableCount));
    }

    [RelayCommand]
    private async Task ExportTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFolderPath))
        {
            _mainViewModel.AppendLog("エクスポートフォルダを選択してください。", LogLevel.Error);
            return;
        }

        var selectedTables = Tables.Where(t => t.IsSelected).ToList();
        if (selectedTables.Count == 0)
        {
            _mainViewModel.AppendLog("エクスポートするテーブルを選択してください。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportDir = Path.Combine(ExportFolderPath, timestamp);
            Directory.CreateDirectory(exportDir);

            _mainViewModel.AppendLog($"エクスポートを開始しています... ({selectedTables.Count} テーブル)", LogLevel.Info);
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            foreach (var table in selectedTables)
            {
                var csvPath = Path.Combine(exportDir, $"{table.TableName}.csv");
                await _databaseService.ExportTableToCsvAsync(
                    connectionString,
                    table.SchemaName,
                    table.TableName,
                    csvPath,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                ProgressValue = (int)(completed * 100 / selectedTables.Count);
            }

            _mainViewModel.AppendLog($"{selectedTables.Count} 個のテーブルをエクスポートしました。保存先: {exportDir}", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"エクスポートに失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }

    [RelayCommand]
    private void SelectExportFolder()
    {
        var selected = FolderPickerService.PickFolder("エクスポートフォルダを選択");
        if (string.IsNullOrWhiteSpace(selected))
        {
            _mainViewModel.AppendLog("エクスポートフォルダの選択をキャンセルしました。", LogLevel.Warning);
            return;
        }

        ExportFolderPath = selected;
    }

    [RelayCommand]
    private void SelectImportFolder()
    {
        var selected = FolderPickerService.PickFolder("インポートフォルダを選択");
        if (string.IsNullOrWhiteSpace(selected))
        {
            _mainViewModel.AppendLog("インポートフォルダの選択をキャンセルしました。", LogLevel.Warning);
            return;
        }

        ImportFolderPath = selected;
    }

    [RelayCommand]
    private async Task ImportTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            _mainViewModel.AppendLog("インポートフォルダを選択してください。", LogLevel.Error);
            return;
        }

        var csvFiles = Directory.GetFiles(ImportFolderPath, "*.csv", SearchOption.AllDirectories);
        if (csvFiles.Length == 0)
        {
            _mainViewModel.AppendLog("CSV ファイルが見つかりません。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"インポートを開始しています... ({csvFiles.Length} ファイル)", LogLevel.Info);
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            string schemaName = GetSchemaFromUsername(SelectedConnection.User);

            foreach (var csvFile in csvFiles)
            {
                var tableName = Path.GetFileNameWithoutExtension(csvFile);

                _mainViewModel.AppendLog($"{csvFile} をインポートしています...", LogLevel.Info);

                await _databaseService.ImportTableFromCsvAsync(
                    connectionString,
                    schemaName,
                    tableName,
                    csvFile,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                ProgressValue = (int)(completed * 100 / csvFiles.Length);
            }

            _mainViewModel.AppendLog($"インポートが完了しました。{csvFiles.Length} ファイル処理しました。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"インポートに失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }

    private static string GetSchemaFromUsername(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "public";
        }

        return userName.StartsWith("cis", StringComparison.OrdinalIgnoreCase)
            ? "unisys"
            : "public";
    }
}

