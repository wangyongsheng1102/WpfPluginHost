using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Plugin.Abstractions;
using Plugin.PixelCompare.Models;
using Plugin.PixelCompare.Services;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Plugin.PixelCompare.ViewModels;

public partial class PixelCompareViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext? _context;
    private readonly ExcelImageExtractorService _excelExtractorService = new();
    private readonly ImageComparisonService _imageComparisonService = new(new DifferenceRegionService(), new ImageAnnotationService());
    private readonly HtmlReportService _htmlReportService = new();
    private readonly CompareOptions _options = new();
    private bool _isDisposed;

    public ObservableCollection<string> AvailableSheets { get; } = new();
    public ObservableCollection<string> AvailableColumns { get; } = new();
    public ObservableCollection<CompareRowItem> CompareItems { get; } = new();

    public event EventHandler? CompareCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReloadSheetNames))]
    private string _excelPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    private string _selectedSheet = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    private string _leftColumnName = "B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    private string _rightColumnName = "P";

    [ObservableProperty]
    private CompareRowItem? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    [NotifyPropertyChangedFor(nameof(CanReloadSheetNames))]
    private bool _isProcessing;

    [ObservableProperty]
    private string? _previewImagePath;

    [ObservableProperty]
    private bool _showLeftPreview = true;

    public bool HasItems => CompareItems.Count > 0;
    public bool CanStartCompare =>
        !IsProcessing &&
        File.Exists(ExcelPath) &&
        !string.IsNullOrWhiteSpace(SelectedSheet) &&
        !string.IsNullOrWhiteSpace(LeftColumnName) &&
        !string.IsNullOrWhiteSpace(RightColumnName);
    public bool CanReloadSheetNames =>
        !IsProcessing &&
        File.Exists(ExcelPath) &&
        AvailableSheets.Count > 0;

    public PixelCompareViewModel(IPluginContext? context)
    {
        _context = context;
        // 常用範囲の列名をプリセット (A-Z, AA-ZZ)
        for (var c = 'A'; c <= 'Z'; c++)
        {
            AvailableColumns.Add(c.ToString());
        }
        for (var c1 = 'A'; c1 <= 'Z'; c1++)
        {
            for (var c2 = 'A'; c2 <= 'Z'; c2++)
            {
                AvailableColumns.Add($"{c1}{c2}");
            }
        }

        CompareItems.CollectionChanged += OnCompareItemsCollectionChanged;
        AvailableSheets.CollectionChanged += OnAvailableSheetsCollectionChanged;
    }

    partial void OnSelectedItemChanged(CompareRowItem? value)
    {
        if (value is null)
        {
            PreviewImagePath = null;
            return;
        }

        PreviewImagePath = ShowLeftPreview ? value.MarkedImage1Path : value.MarkedImage2Path;
    }

    partial void OnShowLeftPreviewChanged(bool value)
    {
        if (SelectedItem is not null)
        {
            PreviewImagePath = value ? SelectedItem.MarkedImage1Path : SelectedItem.MarkedImage2Path;
        }
    }

    partial void OnSelectedSheetChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsProcessing)
        {
            return;
        }

        if (!File.Exists(ExcelPath) || string.IsNullOrWhiteSpace(LeftColumnName) || string.IsNullOrWhiteSpace(RightColumnName))
        {
            return;
        }

        _ = RunCompareAsync();
    }

    [RelayCommand]
    private void ShowLeftImage()
    {
        ShowLeftPreview = true;
    }

    [RelayCommand]
    private void ShowRightImage()
    {
        ShowLeftPreview = false;
    }

    [RelayCommand]
    private async Task SelectExcelAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel ファイル (*.xlsx;*.xls;*.xlsm)|*.xlsx;*.xls;*.xlsm|すべてのファイル (*.*)|*.*",
            Multiselect = false,
            Title = "Excelファイルを選択"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ExcelPath = dialog.FileName;
        await ReloadSheetNamesAsync();
    }

    [RelayCommand]
    private async Task ReloadSheetNamesAsync()
    {
        if (!File.Exists(ExcelPath))
        {
            _context?.ReportWarning("Excelファイルが存在しません。");
            return;
        }

        var loadOk = false;
        try
        {
            IsProcessing = true;
            _context?.ReportProgress("比較可能なシートを読み込み中です...", 0, true);

            var sheets = await _excelExtractorService.GetComparableSheetNamesAsync(ExcelPath, LeftColumnName, RightColumnName);
            AvailableSheets.Clear();
            foreach (var sheet in sheets)
            {
                AvailableSheets.Add(sheet);
            }

            SelectedSheet = AvailableSheets.FirstOrDefault() ?? string.Empty;
            var msg = AvailableSheets.Count > 0 ? $"比較可能なシートを {AvailableSheets.Count} 件読み込みました。" : "比較可能なシートが見つかりません。";
            _context?.ReportInfo(msg);
            loadOk = AvailableSheets.Count > 0;
        }
        catch (Exception ex)
        {
            _context?.ReportError($"シートの読み込みに失敗しました: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }

        if (loadOk)
        {
            await RunCompareAsync();
        }
    }

    private async Task RunCompareAsync()
    {
        if (!CanStartCompare)
        {
            return;
        }

        try
        {
            IsProcessing = true;
            CleanupCurrentTempFiles();
            CompareItems.Clear();
            PreviewImagePath = null;
            OnPropertyChanged(nameof(HasItems));
            ExportReportCommand.NotifyCanExecuteChanged();

            _context?.ReportProgress("画像を抽出中です...", 5, true);
            var pairs = await _excelExtractorService.ExtractPairsAsync(ExcelPath, SelectedSheet, LeftColumnName, RightColumnName);
            if (pairs.Count == 0)
            {
                _context?.ReportWarning("このシートには比較対象データがありません。");
                return;
            }

            foreach (var pair in pairs.OrderBy(x => x.RowIndex))
            {
                CompareItems.Add(new CompareRowItem
                {
                    RowIndex = pair.RowIndex,
                    Image1Path = pair.Image1Path,
                    Image2Path = pair.Image2Path
                });
            }

            await RunComparisonForItemsAsync(CompareItems);
            OnPropertyChanged(nameof(HasItems));
            ExportReportCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _context?.ReportError($"比較に失敗しました: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML ファイル (*.html)|*.html",
            FileName = $"PixelCompareReport_{DateTime.Now:yyyyMMdd_HHmmss}.html",
            Title = "HTMLレポートを出力"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsProcessing = true;
            _context?.ReportProgress("HTMLレポートを出力中です...", 0, true);

            var sheets = AvailableSheets.ToList();
            if (sheets.Count == 0)
            {
                _context?.ReportWarning("比較可能なシートがありません。");
                return;
            }

            var allResults = new List<(string SheetName, int RowIndex, ComparisonResult Result)>();
            var totalSheets = sheets.Count;
            var sheetsProcessed = 0;

            foreach (var sheet in sheets)
            {
                var currentSheetIdx = Interlocked.Increment(ref sheetsProcessed);
                var progress = (double)(currentSheetIdx - 1) / totalSheets * 100.0;
                _context?.ReportProgress($"レポート作成準備中 ({currentSheetIdx}/{totalSheets}): {sheet}", progress, true);

                var pairs = await _excelExtractorService.ExtractPairsAsync(ExcelPath, sheet, LeftColumnName, RightColumnName);
                if (pairs.Count == 0) continue;

                var sheetResults = new List<(string SheetName, int RowIndex, ComparisonResult Result)>();
                using var semaphore = new SemaphoreSlim(5);
                var compareTasks = pairs.Select(async pair =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var result = await _imageComparisonService.CompareAsync(pair.Image1Path, pair.Image2Path, pair.RowIndex, _options);
                        lock (sheetResults)
                        {
                            sheetResults.Add((sheet, pair.RowIndex, result));
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(compareTasks);
                lock (allResults)
                {
                    allResults.AddRange(sheetResults);
                }
            }

            _context?.ReportProgress("HTMLレポートを生成中...", 95, true);
            await _htmlReportService.ExportAsync(dialog.FileName, ExcelPath, allResults.OrderBy(x => x.SheetName).ThenBy(x => x.RowIndex).ToList());
            CleanupCurrentTempFiles();
            CleanupReportTempFiles(allResults.Select(x => x.Result));
            _context?.ReportSuccess("レポートの出力が完了しました。");
        }
        catch (Exception ex)
        {
            _context?.ReportError($"レポートの出力に失敗しました: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanExportReport() => !IsProcessing && CompareItems.Any(x => x.IsComparisonLoaded);

    private async Task RunComparisonForItemsAsync(IEnumerable<CompareRowItem> items)
    {
        var itemList = items.ToList();
        var total = itemList.Count;
        var completedCount = 0;
        using var semaphore = new SemaphoreSlim(5);
        var tasks = itemList.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                await RunOnUiAsync(() => item.IsLoading = true);
                var result = await _imageComparisonService.CompareAsync(item.Image1Path, item.Image2Path, item.RowIndex, _options);
                await RunOnUiAsync(() =>
                {
                    item.IsLoading = false;
                    item.IsComparisonLoaded = true;
                    item.DiffCount = result.DiffCount;
                    item.DifferencePercentage = result.DifferencePercentage;
                    item.IsSizeMismatch = result.IsSizeMismatch;
                    item.SizeInfo = result.SizeInfo;
                    item.ErrorMessage = result.HasError ? result.ErrorMessage : null;
                    item.DifferenceImagePath = result.DifferenceImagePath;
                    item.MarkedImage1Path = result.MarkedImage1Path;
                    item.MarkedImage2Path = result.MarkedImage2Path;
                });
            }
            finally
            {
                var done = Interlocked.Increment(ref completedCount);
                var progress = total == 0 ? 0 : done * 100.0 / total;
                await RunOnUiAsync(() => _context?.ReportProgress($"比較処理中... {done}/{total}", progress));
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        await RunOnUiAsync(() =>
        {
            _context?.ReportSuccess($"比較が完了しました。合計 {total} 件です。");
            SelectedItem = CompareItems.FirstOrDefault();
            CompareCompleted?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// 行の INotifyPropertyChanged とホストの進捗表示は UI スレッド必須。バックグラウンドから触ると DataGrid 操作時にクラッシュする。
    /// </summary>
    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.DataBind).Task;
    }

    private void OnCompareItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    private void OnAvailableSheetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanReloadSheetNames));
    }

    private void CleanupCurrentTempFiles()
    {
        var paths = CompareItems
            .SelectMany(x => new[]
            {
                x.Image1Path,
                x.Image2Path,
                x.DifferenceImagePath,
                x.MarkedImage1Path,
                x.MarkedImage2Path
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in paths)
        {
            try
            {
                if (path is not null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 他プロセスがロックしている場合などは無視し、処理を継続する。
            }
        }
    }

    private static void CleanupReportTempFiles(IEnumerable<ComparisonResult> results)
    {
        var paths = results
            .SelectMany(x => new[]
            {
                x.DifferenceImagePath,
                x.MarkedImage1Path,
                x.MarkedImage2Path,
                x.Image1Path,
                x.Image2Path
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in paths)
        {
            try
            {
                if (path is not null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時ファイル削除失敗時もレポート出力結果は維持する。
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        CleanupCurrentTempFiles();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
