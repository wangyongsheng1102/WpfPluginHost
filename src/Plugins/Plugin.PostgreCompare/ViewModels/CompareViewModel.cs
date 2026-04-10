using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;

namespace Plugin.PostgreCompare.ViewModels;

public partial class CompareViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private string _baseFolderPath = string.Empty;

    [ObservableProperty]
    private string _oldFolderPath = string.Empty;

    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CsvFileInfo> _csvFileInfos = new();

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    private bool _accessLogChecked;

    public int SelectedCsvFileCount => CsvFileInfos.Count(f => f.IsSelected);

    private readonly CsvCompareService _csvCompareService = new();
    private readonly DatabaseService _databaseService = new();
    private readonly ExcelExportService _excelService = new();
    private readonly MainViewModel _mainViewModel;

    public CompareViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    [RelayCommand]
    private void SelectBaseFolder()
    {
        var selected = FolderPickerService.PickFolder("更新前フォルダを選択");
        if (string.IsNullOrWhiteSpace(selected))
        {
            _mainViewModel.AppendLog("更新前フォルダ選択：任意のファイルを選ぶと、そのフォルダが設定されます。", LogLevel.Warning);
            return;
        }

        BaseFolderPath = selected;
        LoadCsvFilePairs();
    }

    [RelayCommand]
    private void SelectOldFolder()
    {
        var selected = FolderPickerService.PickFolder("旧フォルダを選択");
        if (string.IsNullOrWhiteSpace(selected))
        {
            _mainViewModel.AppendLog("旧フォルダ選択：任意のファイルを選ぶと、そのフォルダが設定されます。", LogLevel.Warning);
            return;
        }

        OldFolderPath = selected;
        LoadCsvFilePairs();
    }

    [RelayCommand]
    private void SelectNewFolder()
    {
        var selected = FolderPickerService.PickFolder("新フォルダを選択");
        if (string.IsNullOrWhiteSpace(selected))
        {
            _mainViewModel.AppendLog("新フォルダ選択：任意のファイルを選ぶと、そのフォルダが設定されます。", LogLevel.Warning);
            return;
        }

        NewFolderPath = selected;
        LoadCsvFilePairs();
    }

    [RelayCommand]
    private void SelectExportFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "比較結果の保存先を選択",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() == true)
        {
            ExportFilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void RefreshCsvFiles()
    {
        LoadCsvFilePairs();
    }

    private void LoadCsvFilePairs()
    {
        if (string.IsNullOrWhiteSpace(BaseFolderPath) ||
            string.IsNullOrWhiteSpace(OldFolderPath) ||
            string.IsNullOrWhiteSpace(NewFolderPath))
        {
            return;
        }

        try
        {
            var baseFiles = Directory.GetFiles(BaseFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToHashSet();

            var oldFiles = Directory.GetFiles(OldFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToHashSet();

            var newFiles = Directory.GetFiles(NewFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToHashSet();

            var commonFiles = baseFiles.Intersect(oldFiles).Intersect(newFiles).OrderBy(f => f).ToList();

            CsvFileInfos.Clear();
            foreach (var file in commonFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                var fileInfo = new CsvFileInfo(file);
                fileInfo.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CsvFileInfo.IsSelected))
                    {
                        OnPropertyChanged(nameof(SelectedCsvFileCount));
                    }
                };
                CsvFileInfos.Add(fileInfo);
            }

            OnPropertyChanged(nameof(SelectedCsvFileCount));
            _mainViewModel.AppendLog($"{CsvFileInfos.Count} 個の共通 CSV ファイルを検出しました。", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"ファイルリストの取得に失敗しました: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private void SelectAllCsvFiles()
    {
        foreach (var fileInfo in CsvFileInfos)
        {
            fileInfo.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedCsvFileCount));
    }

    [RelayCommand]
    private void DeselectAllCsvFiles()
    {
        foreach (var fileInfo in CsvFileInfos)
        {
            fileInfo.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedCsvFileCount));
    }

    [RelayCommand]
    private async Task CompareCsvFiles()
    {
        LoadCsvFilePairs();

        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("データベース接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseFolderPath) ||
            string.IsNullOrWhiteSpace(OldFolderPath) ||
            string.IsNullOrWhiteSpace(NewFolderPath))
        {
            _mainViewModel.AppendLog("更新前フォルダ、旧フォルダ、新フォルダのすべてを選択してください。", LogLevel.Error);
            return;
        }

        var allFiles = CsvFileInfos.ToList();
        if (allFiles.Count == 0)
        {
            _mainViewModel.AppendLog("比較する CSV ファイルが見つかりません。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            _mainViewModel.AppendLog("エクスポートファイルパスを指定してください。", LogLevel.Error);
            return;
        }

        ExportFilePath = NormalizeExcelExportPath(ExportFilePath);

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"[{allFiles.Count} 件] CSV ファイルの比較を開始しています...", LogLevel.Info);
            _mainViewModel.ReportProgress("CSV ファイルの比較を開始しています...", 0, true);

            if (IsFileLocked(ExportFilePath))
            {
                _mainViewModel.AppendLog("Excel ファイルは LOCK していますので、チェックしてください...", LogLevel.Error);
                return;
            }

            var connectionString = SelectedConnection.GetConnectionString();
            _mainViewModel.AppendLog("データベース接続を確認しています...", LogLevel.Info);
            var (dbOk, dbError) = await _databaseService.CheckDatabaseReachableAsync(connectionString);
            if (!dbOk)
            {
                _mainViewModel.AppendLog(
                    $"データベースに接続できません。PostgreSQL が起動しているか、ホスト・ポート・DB 名・認証情報を確認してください。詳細: {dbError}",
                    LogLevel.Error);
                return;
            }

            _mainViewModel.AppendLog("データベース接続を確認しました。", LogLevel.Success);

            await Task.Run(async () =>
            {
                var preferredSchemaName = GetSchemaFromUsername(SelectedConnection.User);
                var baseFileMap = BuildCsvLookup(BaseFolderPath);
                var oldFileMap = BuildCsvLookup(OldFolderPath);
                var newFileMap = BuildCsvLookup(NewFolderPath);
                var baseVsOldResults = new List<RowComparisonResult>();
                var baseVsNewResults = new List<RowComparisonResult>();
                var completed = 0;

                foreach (var csvFileInfo in allFiles)
                {
                    if (!AccessLogChecked && csvFileInfo.FileName.Contains("access_log.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        _mainViewModel.AppendLog("access_log.csv の比較はスキップされました。", LogLevel.Warning);
                        completed++;
                        _mainViewModel.ReportProgress($"比較中... ({completed}/{allFiles.Count})", (double)completed * 100 / allFiles.Count);
                        continue;
                    }

                    var csvFileName = csvFileInfo.FileName;
                    _mainViewModel.AppendLog($"{csvFileName} を比較しています...", LogLevel.Info);

                    baseFileMap.TryGetValue(csvFileName, out var baseCsvPath);
                    oldFileMap.TryGetValue(csvFileName, out var oldCsvPath);
                    newFileMap.TryGetValue(csvFileName, out var newCsvPath);

                    if (baseCsvPath == null || oldCsvPath == null || newCsvPath == null)
                    {
                        _mainViewModel.AppendLog($"{csvFileName} が見つかりません。スキップします。", LogLevel.Warning);
                        completed++;
                        _mainViewModel.ReportProgress($"比較中... ({completed}/{allFiles.Count})", (double)completed * 100 / allFiles.Count);
                        continue;
                    }

                    var tableName = Path.GetFileNameWithoutExtension(csvFileName) ?? string.Empty;
                    var schemaName = preferredSchemaName;

                    List<string>? primaryKeys = null;
                    try
                    {
                        var resolvedTable = await _databaseService.ResolvePrimaryKeyColumnsAsync(
                            connectionString,
                            preferredSchemaName,
                            tableName);
                        schemaName = resolvedTable.SchemaName;
                        primaryKeys = resolvedTable.PrimaryKeys;

                        if (!string.Equals(schemaName, preferredSchemaName, StringComparison.OrdinalIgnoreCase))
                        {
                            _mainViewModel.AppendLog(
                                $"テーブル '{tableName}' は推定スキーマ '{preferredSchemaName}' ではなく '{schemaName}' で解決しました。",
                                LogLevel.Info);
                        }

                        if (primaryKeys.Count > 0)
                        {
                            _mainViewModel.AppendLog(
                                $"テーブル '{schemaName}.{tableName}' の主キー: {string.Join(", ", primaryKeys)}",
                                LogLevel.Info);
                        }
                        else
                        {
                            _mainViewModel.AppendLog(
                                $"テーブル '{schemaName}.{tableName}' に主キーが見つかりません。整行比較モードを使用します。",
                                LogLevel.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _mainViewModel.AppendLog(
                            $"テーブル '{schemaName}.{tableName}' の主キー取得に失敗しました: {ex.Message}。整行比較モードを使用します。",
                            LogLevel.Warning);
                    }

                    var fileProgress1 = new Progress<(int current, int total, string message)>(p =>
                    {
                        var overall = allFiles.Count > 0 ? (completed * 200 + p.current) / (allFiles.Count * 2) : 0;
                        _mainViewModel.ReportProgress($"[{csvFileName} - Base vs Old] {p.message}", overall);
                    });

                    var results1 = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        oldCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        fileProgress1);

                    baseVsOldResults.AddRange(results1);

                    var fileProgress2 = new Progress<(int current, int total, string message)>(p =>
                    {
                        var overall = allFiles.Count > 0 ? (completed * 200 + 100 + p.current) / (allFiles.Count * 2) : 0;
                        _mainViewModel.ReportProgress($"[{csvFileName} - Base vs New] {p.message}", overall);
                    });

                    var results2 = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        newCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        fileProgress2);

                    baseVsNewResults.AddRange(results2);

                    completed++;
                    _mainViewModel.ReportProgress($"比較中... ({completed}/{allFiles.Count})", (double)completed * 100 / allFiles.Count);
                }

                _mainViewModel.AppendLog("Excel ファイルを生成しています...", LogLevel.Info);
                _excelService.ExportComparisonResults(ExportFilePath, baseVsOldResults, baseVsNewResults, connectionString);

                var oldDeletedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Deleted);
                var oldAddedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Added);
                var oldUpdatedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Updated);
                var newDeletedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Deleted);
                var newAddedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Added);
                var newUpdatedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Updated);

                _mainViewModel.AppendLog("比較が完了しました。", LogLevel.Success);
                _mainViewModel.AppendLog($"更新前 vs 旧: 削除={oldDeletedCount}, 追加={oldAddedCount}, 更新={oldUpdatedCount}", LogLevel.Info);
                _mainViewModel.AppendLog($"更新前 vs 新: 削除={newDeletedCount}, 追加={newAddedCount}, 更新={newUpdatedCount}", LogLevel.Info);
                _mainViewModel.AppendLog($"結果を {ExportFilePath} に保存しました。", LogLevel.Success);
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"比較処理でエラーが発生しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            _mainViewModel.ReportProgress(string.Empty, 100);
        }
    }

    private static Dictionary<string, string> BuildCsvLookup(string folderPath)
    {
        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*.csv", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static string NormalizeExcelExportPath(string filePath)
    {
        var trimmedPath = filePath.Trim();
        var extension = Path.GetExtension(trimmedPath);

        if (string.IsNullOrEmpty(extension) || string.Equals(extension, ".", StringComparison.Ordinal))
        {
            return $"{trimmedPath.TrimEnd('.')}.xlsx";
        }

        return trimmedPath;
    }

    /// <summary>
    /// 参照: WslPostgreTool CompareViewModel.GetSchemaFromUsername
    /// </summary>
    private static string GetSchemaFromUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return "public";

        if (username.StartsWith("cis", StringComparison.OrdinalIgnoreCase))
            return "unisys";
        if (username.StartsWith("order", StringComparison.OrdinalIgnoreCase))
            return "public";
        if (username.StartsWith("portal", StringComparison.OrdinalIgnoreCase))
            return "public";

        return "public";
    }
}
