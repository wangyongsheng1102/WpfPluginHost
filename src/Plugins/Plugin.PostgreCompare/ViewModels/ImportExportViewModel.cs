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
    private ObservableCollection<string> _schemas = new();

    [ObservableProperty]
    private string? _selectedSchema;

    [ObservableProperty]
    private ObservableCollection<TableInfo> _tables = new();

    public int SelectedTableCount => Tables.Count(t => t.IsSelected);

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

    async partial void OnSelectedConnectionChanged(DatabaseConnection? value)
    {
        Schemas.Clear();
        SelectedSchema = null;
        Tables.Clear();

        if (value != null)
        {
            try
            {
                IsProcessing = true;
                _mainViewModel.ReportProgress("スキーマリストを取得しています...", 0, true);
                var schemas = await _databaseService.GetSchemasAsync(value.GetConnectionString());
                foreach (var schema in schemas)
                {
                    Schemas.Add(schema);
                }
                SelectedSchema = Schemas.FirstOrDefault(s => s == "public") ?? Schemas.FirstOrDefault();
            }
            finally
            {
                IsProcessing = false;
                _mainViewModel.ReportProgress(string.Empty, 100);
            }
        }
    }

    async partial void OnSelectedSchemaChanged(string? value)
    {
        if (value != null && SelectedConnection != null)
        {
            await LoadTables();
        }
        else
        {
            Tables.Clear();
        }
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null || string.IsNullOrEmpty(SelectedSchema))
        {
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.ReportProgress("テーブルリストを取得しています...", 0, true);

            var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString(), SelectedSchema);
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
            _mainViewModel.AppendLog($"{Tables.Count} 個のテーブルを取得しました。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"テーブル取得に失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            _mainViewModel.ReportProgress(string.Empty, 100);
        }
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
        if (SelectedConnection == null || string.IsNullOrEmpty(SelectedSchema))
        {
            _mainViewModel.AppendLog("接続とスキーマを選択してください。", LogLevel.Error);
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
            _mainViewModel.ReportProgress($"エクスポート中... (0/{selectedTables.Count})", 0, false);

            var connectionString = SelectedConnection.GetConnectionString();
            await using var conn = await _databaseService.OpenConnectionAsync(connectionString);
            int completed = 0;

            foreach (var table in selectedTables)
            {
                var csvPath = Path.Combine(exportDir, $"{table.TableName}.csv");
                await _databaseService.ExportTableToCsvAsync(
                    conn,
                    table.SchemaName,
                    table.TableName,
                    csvPath,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                var percentage = (double)completed * 100 / selectedTables.Count;
                _mainViewModel.ReportProgress($"エクスポート中... ({completed}/{selectedTables.Count})", percentage);
            }

            _mainViewModel.AppendLog($"{selectedTables.Count} 個のテーブルをエクスポートしました。保存先: {exportDir}", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"エクスポートに失败しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            _mainViewModel.ReportProgress(string.Empty, 100);
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
        if (SelectedConnection == null || string.IsNullOrEmpty(SelectedSchema))
        {
            _mainViewModel.AppendLog("接続とスキーマを選択してください。", LogLevel.Error);
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
            _mainViewModel.ReportProgress($"インポート中... (0/{csvFiles.Length})", 0, false);

            var connectionString = SelectedConnection.GetConnectionString();
            await using var conn = await _databaseService.OpenConnectionAsync(connectionString);
            int completed = 0;

            foreach (var csvFile in csvFiles)
            {
                var tableName = Path.GetFileNameWithoutExtension(csvFile);
                await _databaseService.ImportTableFromCsvAsync(
                    conn,
                    SelectedSchema,
                    tableName,
                    csvFile,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                var percentage = (double)completed * 100 / csvFiles.Length;
                _mainViewModel.ReportProgress($"インポート中... ({completed}/{csvFiles.Length})", percentage);
            }

            _mainViewModel.AppendLog($"{csvFiles.Length} 個のファイルをインポートしました。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"インポートに失败しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            _mainViewModel.ReportProgress(string.Empty, 100);
        }
    }
}
