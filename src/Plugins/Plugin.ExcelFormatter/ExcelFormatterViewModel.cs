using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Plugin.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.ExcelFormatter;

public partial class ExcelFileItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    private string _status = "待機中";

    [ObservableProperty]
    private string? _statusDetail;
}

public partial class ExcelFormatterViewModel : ObservableObject
{
    private readonly IPluginContext? _context;
    private bool _suppressSelectAllSync;

    public ExcelFormatterViewModel(IPluginContext? context = null)
    {
        _context = context;
    }

    // ── 表示設定 ────────────────────────────────────
    [ObservableProperty] private bool _optZoomReset = true;
    [ObservableProperty] private int _zoomLevel = 100;
    [ObservableProperty] private bool _optCursorA1 = true;
    [ObservableProperty] private bool _optActiveFirstSheet = true;
    [ObservableProperty] private bool _optScrollTopLeft = true;

    // ── データ整理 ──────────────────────────────────
    [ObservableProperty] private bool _optClearFilters = true;
    [ObservableProperty] private bool _optFormulaToValue = true;
    [ObservableProperty] private bool _optBreakExternalLinks = true;
    [ObservableProperty] private bool _optRemoveComments = false;
    [ObservableProperty] private bool _optClearConditionalFormats = false;

    // ── レイアウト調整 ──────────────────────────────
    [ObservableProperty] private bool _optAutoFitColumns = true;
    [ObservableProperty] private bool _optAutoFitRows = true;
    [ObservableProperty] private bool _optUnfreezePanes = false;
    [ObservableProperty] private bool _optUnhideSheets = false;
    [ObservableProperty] private bool _optUnhideRowsCols = false;

    // ── 印刷設定 ────────────────────────────────────
    [ObservableProperty] private bool _optResetPrintArea = false;
    [ObservableProperty] private bool _optSetLandscape = false;
    [ObservableProperty] private bool _optFitToOnePage = false;
    [ObservableProperty] private bool _optResetMargins = false;
    [ObservableProperty] private bool _optSetHeaderFooter = false;

    // ── 全選択 ──────────────────────────────────────
    [ObservableProperty] private bool _selectAll = false;

    partial void OnSelectAllChanged(bool value)
    {
        if (_suppressSelectAllSync) return;
        _suppressSelectAllSync = true;

        OptZoomReset = value;
        OptCursorA1 = value;
        OptActiveFirstSheet = value;
        OptScrollTopLeft = value;

        OptClearFilters = value;
        OptFormulaToValue = value;
        OptBreakExternalLinks = value;
        OptRemoveComments = value;
        OptClearConditionalFormats = value;

        OptAutoFitColumns = value;
        OptAutoFitRows = value;
        OptUnfreezePanes = value;
        OptUnhideSheets = value;
        OptUnhideRowsCols = value;

        OptResetPrintArea = value;
        OptSetLandscape = value;
        OptFitToOnePage = value;
        OptResetMargins = value;
        OptSetHeaderFooter = value;

        _suppressSelectAllSync = false;
    }

    private void SyncSelectAllState()
    {
        if (_suppressSelectAllSync) return;
        _suppressSelectAllSync = true;

        var all = OptZoomReset && OptCursorA1 && OptActiveFirstSheet && OptScrollTopLeft
               && OptClearFilters && OptFormulaToValue && OptBreakExternalLinks && OptRemoveComments && OptClearConditionalFormats
               && OptAutoFitColumns && OptAutoFitRows && OptUnfreezePanes && OptUnhideSheets && OptUnhideRowsCols
               && OptResetPrintArea && OptSetLandscape && OptFitToOnePage && OptResetMargins && OptSetHeaderFooter;
        SelectAll = all;

        _suppressSelectAllSync = false;
    }

    partial void OnOptZoomResetChanged(bool value) => SyncSelectAllState();
    partial void OnOptCursorA1Changed(bool value) => SyncSelectAllState();
    partial void OnOptActiveFirstSheetChanged(bool value) => SyncSelectAllState();
    partial void OnOptScrollTopLeftChanged(bool value) => SyncSelectAllState();
    partial void OnOptClearFiltersChanged(bool value) => SyncSelectAllState();
    partial void OnOptFormulaToValueChanged(bool value) => SyncSelectAllState();
    partial void OnOptBreakExternalLinksChanged(bool value) => SyncSelectAllState();
    partial void OnOptRemoveCommentsChanged(bool value) => SyncSelectAllState();
    partial void OnOptClearConditionalFormatsChanged(bool value) => SyncSelectAllState();
    partial void OnOptAutoFitColumnsChanged(bool value) => SyncSelectAllState();
    partial void OnOptAutoFitRowsChanged(bool value) => SyncSelectAllState();
    partial void OnOptUnfreezePanesChanged(bool value) => SyncSelectAllState();
    partial void OnOptUnhideSheetsChanged(bool value) => SyncSelectAllState();
    partial void OnOptUnhideRowsColsChanged(bool value) => SyncSelectAllState();
    partial void OnOptResetPrintAreaChanged(bool value) => SyncSelectAllState();
    partial void OnOptSetLandscapeChanged(bool value) => SyncSelectAllState();
    partial void OnOptFitToOnePageChanged(bool value) => SyncSelectAllState();
    partial void OnOptResetMarginsChanged(bool value) => SyncSelectAllState();
    partial void OnOptSetHeaderFooterChanged(bool value) => SyncSelectAllState();

    // ── ファイルキュー ──────────────────────────────
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isDragOverDropZone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingFiles))]
    private ObservableCollection<ExcelFileItem> _pendingFiles = new();

    public bool HasPendingFiles => PendingFiles.Count > 0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── コマンド ────────────────────────────────────

    [RelayCommand]
    private void SelectExcelFallback()
    {
        if (IsProcessing) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel ファイル (*.xlsx;*.xls;*.xlsm)|*.xlsx;*.xls;*.xlsm|すべてのファイル (*.*)|*.*",
            Title = "Excel ファイルを手動で選択",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            _ = HandleDroppedPathsAsync(dlg.FileNames);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        if (IsProcessing) return;
        PendingFiles.Clear();
        OnPropertyChanged(nameof(HasPendingFiles));
        _context?.ClearStatus();
    }

    [RelayCommand]
    private async Task ExecuteFormatAsync()
    {
        if (IsProcessing || !HasPendingFiles) return;

        IsProcessing = true;
        _context?.ReportProgress("一括フォーマットを開始します...", 0, true);

        await Task.Run(() => ProcessFiles(PendingFiles.ToList()));
        IsProcessing = false;
    }

    public async Task HandleDroppedPathsAsync(string[] paths)
    {
        if (IsProcessing) return;
        IsProcessing = true;
        _context?.ReportProgress("ファイルをスキャン中...", 0, true);

        var excelFiles = new List<string>();
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        excelFiles.AddRange(Directory.GetFiles(path, "*.xls*", SearchOption.AllDirectories)
                            .Where(f => !Path.GetFileName(f).StartsWith("~")));
                    }
                    catch { }
                }
                else if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path);
                    if (ext.Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
                    {
                        excelFiles.Add(path);
                    }
                }
            }
        });

        int newCount = 0;
        foreach (var file in excelFiles)
        {
            if (!PendingFiles.Any(p => p.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                PendingFiles.Add(new ExcelFileItem { FilePath = file });
                newCount++;
            }
        }

        OnPropertyChanged(nameof(HasPendingFiles));

        if (newCount == 0)
            _context?.ReportWarning("有効な新しい Excel ファイルは追加されませんでした。");
        else
            _context?.ReportInfo($"{newCount} 個の新しいファイルをスキャンし、キューに追加しました。");

        IsProcessing = false;
    }

    // ── Excel 処理 ──────────────────────────────────

    private void ReportOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void ProcessFiles(List<ExcelFileItem> files)
    {
        Excel.Application? app = null;
        try
        {
            app = new Excel.Application();
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;

            int count = 0;
            foreach (var item in files)
            {
                count++;
                ReportOnUi(() =>
                {
                    item.Status = "処理中...";
                    item.StatusDetail = null;
                    _context?.ReportProgress($"処理の進捗: {count}/{files.Count} - {item.FileName}", (double)count / files.Count * 100);
                });

                bool success = FormatSingleExcel(app, item.FilePath, out var failureReason);

                ReportOnUi(() =>
                {
                    item.Status = success ? "✅ 完了" : "❌ 失敗";
                    item.StatusDetail = success ? null : failureReason;
                });
            }

            ReportOnUi(() => _context?.ReportSuccess($"すべてのキューの実行が完了しました！合計 {files.Count} 個のファイルを処理しました。"));
        }
        catch (Exception ex)
        {
            ReportOnUi(() => _context?.ReportError($"自動化の重大なエラー：{ex.Message}"));
        }
        finally
        {
            if (app != null)
            {
                app.Quit();
                Marshal.ReleaseComObject(app);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private bool FormatSingleExcel(Excel.Application app, string path, out string? errorMessage)
    {
        errorMessage = null;
        Excel.Workbook? wb = null;
        try
        {
            wb = app.Workbooks.Open(Filename: path, UpdateLinks: Type.Missing, ReadOnly: false);

            // ── シートごとの処理 ────────────────────
            for (int i = 1; i <= wb.Worksheets.Count; i++)
            {
                var ws = (Excel.Worksheet)wb.Worksheets[i];
                Excel.Range? usedRange = null;
                try
                {
                    bool needActivate = OptZoomReset || OptCursorA1 || OptScrollTopLeft || OptUnfreezePanes;
                    if (needActivate)
                        ws.Activate();

                    if (OptZoomReset && app.ActiveWindow != null)
                    {
                        app.ActiveWindow.Zoom = Math.Max(10, Math.Min(400, ZoomLevel));
                    }

                    if (OptScrollTopLeft && app.ActiveWindow != null)
                    {
                        app.ActiveWindow.ScrollRow = 1;
                        app.ActiveWindow.ScrollColumn = 1;
                    }

                    if (OptCursorA1)
                    {
                        var a1 = ws.Range["A1"];
                        a1.Select();
                        Marshal.ReleaseComObject(a1);
                    }

                    if (OptUnfreezePanes && app.ActiveWindow != null)
                    {
                        app.ActiveWindow.FreezePanes = false;
                    }

                    if (OptClearFilters)
                    {
                        try { if (ws.FilterMode) ws.ShowAllData(); } catch { }
                        if (ws.AutoFilterMode) ws.AutoFilterMode = false;
                    }

                    usedRange = ws.UsedRange;
                    if (usedRange != null)
                    {
                        if (OptFormulaToValue)
                            usedRange.Value2 = usedRange.Value2;

                        if (OptAutoFitColumns)
                            usedRange.Columns.AutoFit();

                        if (OptAutoFitRows)
                            usedRange.Rows.AutoFit();
                    }

                    if (OptRemoveComments)
                    {
                        try { ws.Cells.ClearComments(); } catch { }
                    }

                    if (OptClearConditionalFormats)
                    {
                        try { ws.Cells.FormatConditions.Delete(); } catch { }
                    }

                    if (OptUnhideRowsCols)
                    {
                        try
                        {
                            ws.Cells.EntireRow.Hidden = false;
                            ws.Cells.EntireColumn.Hidden = false;
                        }
                        catch { }
                    }

                    // ── 印刷設定 ──
                    if (OptResetPrintArea || OptSetLandscape || OptFitToOnePage || OptResetMargins || OptSetHeaderFooter)
                    {
                        var ps = ws.PageSetup;
                        if (OptResetPrintArea)
                            ps.PrintArea = string.Empty;

                        if (OptSetLandscape)
                            ps.Orientation = Excel.XlPageOrientation.xlLandscape;

                        if (OptFitToOnePage)
                        {
                            ps.FitToPagesWide = 1;
                            ps.FitToPagesTall = 1;
                            ws.PageSetup.Zoom = false;
                        }

                        if (OptResetMargins)
                        {
                            ps.LeftMargin = app.InchesToPoints(0.7);
                            ps.RightMargin = app.InchesToPoints(0.7);
                            ps.TopMargin = app.InchesToPoints(0.75);
                            ps.BottomMargin = app.InchesToPoints(0.75);
                            ps.HeaderMargin = app.InchesToPoints(0.3);
                            ps.FooterMargin = app.InchesToPoints(0.3);
                        }

                        if (OptSetHeaderFooter)
                        {
                            ps.LeftHeader = string.Empty;
                            ps.CenterHeader = "&A";
                            ps.RightHeader = string.Empty;
                            ps.LeftFooter = string.Empty;
                            ps.CenterFooter = "&P / &N";
                            ps.RightFooter = string.Empty;
                        }
                    }
                }
                finally
                {
                    if (usedRange != null) Marshal.ReleaseComObject(usedRange);
                    Marshal.ReleaseComObject(ws);
                }
            }

            // ── シート表示 ──────────────────────────
            if (OptUnhideSheets)
            {
                for (int i = 1; i <= wb.Worksheets.Count; i++)
                {
                    var ws = (Excel.Worksheet)wb.Worksheets[i];
                    try { ws.Visible = Excel.XlSheetVisibility.xlSheetVisible; } catch { }
                    Marshal.ReleaseComObject(ws);
                }
            }

            // ── 外部リンク切断 ──────────────────────
            if (OptBreakExternalLinks)
            {
                var links = wb.LinkSources(Excel.XlLink.xlExcelLinks) as Array;
                if (links != null)
                {
                    for (int i = 1; i <= links.Length; i++)
                    {
                        if (links.GetValue(i) is string linkName)
                        {
                            try { wb.BreakLink(linkName, Excel.XlLinkType.xlLinkTypeExcelLinks); } catch { }
                        }
                    }
                }
            }

            // ── 最初のシートをアクティブ ────────────
            if (OptActiveFirstSheet && wb.Sheets.Count > 0)
            {
                var firstSheet = (Excel.Worksheet)wb.Sheets[1];
                firstSheet.Activate();
                Marshal.ReleaseComObject(firstSheet);
            }

            wb.Save();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
        finally
        {
            if (wb != null)
            {
                wb.Close(SaveChanges: false);
                Marshal.ReleaseComObject(wb);
            }
        }
    }
}
