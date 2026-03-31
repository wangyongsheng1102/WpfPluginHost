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

namespace Plugin.PixelCompare.ViewModels;

public partial class PixelCompareViewModel : ObservableObject
{
    private readonly IPluginContext? _context;
    private readonly ExcelImageExtractorService _excelExtractorService = new();
    private readonly ImageComparisonService _imageComparisonService = new(new DifferenceRegionService(), new ImageAnnotationService());
    private readonly HtmlReportService _htmlReportService = new();
    private readonly CompareOptions _options = new();

    public ObservableCollection<string> AvailableSheets { get; } = new();
    public ObservableCollection<string> AvailableColumns { get; } = new();
    public ObservableCollection<CompareRowItem> CompareItems { get; } = new();

    [ObservableProperty]
    private string _excelPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    [NotifyCanExecuteChangedFor(nameof(StartCompareCommand))]
    private string _selectedSheet = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    [NotifyCanExecuteChangedFor(nameof(StartCompareCommand))]
    private string _leftColumnName = "B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    [NotifyCanExecuteChangedFor(nameof(StartCompareCommand))]
    private string _rightColumnName = "P";

    [ObservableProperty]
    private CompareRowItem? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = "Excelファイルを選択してください。";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompare))]
    [NotifyCanExecuteChangedFor(nameof(StartCompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
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

    public PixelCompareViewModel(IPluginContext? context)
    {
        _context = context;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            AvailableColumns.Add(c.ToString());
        }

        CompareItems.CollectionChanged += OnCompareItemsCollectionChanged;
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
            StatusMessage = "Excelファイルが存在しません。";
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = "比較可能なシートを読み込み中です...";
            _context?.ReportProgress(StatusMessage, 0, true);

            var sheets = await _excelExtractorService.GetComparableSheetNamesAsync(ExcelPath, LeftColumnName, RightColumnName);
            AvailableSheets.Clear();
            foreach (var sheet in sheets)
            {
                AvailableSheets.Add(sheet);
            }

            SelectedSheet = AvailableSheets.FirstOrDefault() ?? string.Empty;
            StatusMessage = AvailableSheets.Count > 0 ? $"比較可能なシートを {AvailableSheets.Count} 件読み込みました。" : "比較可能なシートが見つかりません。";
            _context?.ReportInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"シートの読み込みに失敗しました: {ex.Message}";
            _context?.ReportError(StatusMessage);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCompare))]
    private async Task StartCompareAsync()
    {
        try
        {
            IsProcessing = true;
            ProgressValue = 0;
            CleanupCurrentTempFiles();
            CompareItems.Clear();
            PreviewImagePath = null;
            OnPropertyChanged(nameof(HasItems));
            ExportReportCommand.NotifyCanExecuteChanged();

            StatusMessage = "画像を抽出中です...";
            _context?.ReportProgress(StatusMessage, 5, true);
            var pairs = await _excelExtractorService.ExtractPairsAsync(ExcelPath, SelectedSheet, LeftColumnName, RightColumnName);
            if (pairs.Count == 0)
            {
                StatusMessage = "このシートには比較対象データがありません。";
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
            StatusMessage = $"比較に失敗しました: {ex.Message}";
            _context?.ReportError(StatusMessage);
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
            StatusMessage = "HTMLレポートを出力中です...";
            _context?.ReportProgress(StatusMessage, 0, true);

            var results = CompareItems
                .Where(x => x.IsComparisonLoaded)
                .Select(x => (x.RowIndex, new ComparisonResult
                {
                    DiffCount = x.DiffCount,
                    DifferencePercentage = x.DifferencePercentage,
                    HasError = !string.IsNullOrWhiteSpace(x.ErrorMessage),
                    ErrorMessage = x.ErrorMessage ?? string.Empty,
                    IsSizeMismatch = x.IsSizeMismatch,
                    SizeInfo = x.SizeInfo,
                    MarkedImage1Path = x.MarkedImage1Path,
                    MarkedImage2Path = x.MarkedImage2Path,
                    DifferenceImagePath = x.DifferenceImagePath
                }))
                .ToList();

            await _htmlReportService.ExportAsync(dialog.FileName, ExcelPath, SelectedSheet, results);
            CleanupCurrentTempFiles();
            StatusMessage = "レポートの出力が完了しました。";
            _context?.ReportSuccess(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"レポートの出力に失敗しました: {ex.Message}";
            _context?.ReportError(StatusMessage);
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
        var completed = 0;
        using var semaphore = new SemaphoreSlim(5);
        var tasks = itemList.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                item.IsLoading = true;
                var result = await _imageComparisonService.CompareAsync(item.Image1Path, item.Image2Path, item.RowIndex, _options);
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
            }
            finally
            {
                completed++;
                ProgressValue = total == 0 ? 0 : completed * 100.0 / total;
                StatusMessage = $"比較処理中... {completed}/{total}";
                _context?.ReportProgress(StatusMessage, ProgressValue);
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        StatusMessage = $"比較が完了しました。合計 {total} 件です。";
        _context?.ReportSuccess(StatusMessage);

        SelectedItem = CompareItems.FirstOrDefault();
    }

    private void OnCompareItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ExportReportCommand.NotifyCanExecuteChanged();
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
                // 他进程占用等场景下忽略删除异常，不影响主流程。
            }
        }
    }
}
