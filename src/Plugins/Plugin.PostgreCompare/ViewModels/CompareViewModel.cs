using System;
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
    private int _progressValue;

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

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"[{allFiles.Count} 件] CSV ファイルの比較を開始しています...", LogLevel.Info);
            ProgressValue = 0;

            if (IsFileLocked(ExportFilePath))
            {
                _mainViewModel.AppendLog("Excel ファイルは LOCK していますので、チェックしてください...", LogLevel.Error);
                return;
            }

            await Task.Run(async () =>
            {
                var connectionString = SelectedConnection.GetConnectionString();
                var baseVsOldResults = new System.Collections.Generic.List<RowComparisonResult>();
                var baseVsNewResults = new System.Collections.Generic.List<RowComparisonResult>();
                int completed = 0;

                foreach (var csvFileInfo in allFiles)
                {
                    if (!AccessLogChecked && csvFileInfo.FileName.Contains("access_log.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        _mainViewModel.AppendLog("access_log.csv の比較はスキップされました。", LogLevel.Warning);
                        completed++;
                        ProgressValue = (completed * 200) / (allFiles.Count * 2);
                        continue;
                    }

                    var csvFileName = csvFileInfo.FileName;
                    _mainViewModel.AppendLog($"{csvFileName} を比較しています...", LogLevel.Info);

                    var baseCsvPath = FindCsvFile(BaseFolderPath, csvFileName);
                    var oldCsvPath = FindCsvFile(OldFolderPath, csvFileName);
                    var newCsvPath = FindCsvFile(NewFolderPath, csvFileName);

                    if (baseCsvPath == null || oldCsvPath == null || newCsvPath == null)
                    {
                        _mainViewModel.AppendLog($"{csvFileName} が見つかりません。スキップします。", LogLevel.Warning);
                        completed++;
                        ProgressValue = (completed * 200) / (allFiles.Count * 2);
                        continue;
                    }

                    var schemaName = GetSchemaFromUsername(SelectedConnection.User);

                    var tableName = Path.GetFileNameWithoutExtension(csvFileName) ?? string.Empty;
                    var primaryKeys = await _databaseService.GetPrimaryKeyColumnsAsync(connectionString, schemaName, tableName);

                    var baseVsOld = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        oldCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        new Progress<(int current, int total, string message)>(p =>
                        {
                            _mainViewModel.AppendLog(p.message, LogLevel.Info);
                        }));

                    var baseVsNew = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        newCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        new Progress<(int current, int total, string message)>(p =>
                        {
                            _mainViewModel.AppendLog(p.message, LogLevel.Info);
                        }));

                    baseVsOldResults.AddRange(baseVsOld);
                    baseVsNewResults.AddRange(baseVsNew);

                    completed++;
                    ProgressValue = (completed * 200) / (allFiles.Count * 2);
                }

                _excelService.ExportComparisonResults(ExportFilePath, baseVsOldResults, baseVsNewResults, connectionString);
                _mainViewModel.AppendLog($"比較結果を Excel にエクスポートしました: {ExportFilePath}", LogLevel.Success);
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"比較処理でエラーが発生しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private static string? FindCsvFile(string rootFolder, string fileName)
    {
        return Directory.GetFiles(rootFolder, "*.csv", SearchOption.AllDirectories)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
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

