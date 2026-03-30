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

namespace Plugin.SampleA;

public partial class ExcelFileItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    public string FileName => System.IO.Path.GetFileName(FilePath);

    [ObservableProperty]
    private string _status = "待機中";
}

public partial class SampleAViewModel : ObservableObject
{
    private readonly IPluginContext? _context;

    public SampleAViewModel(IPluginContext? context = null)
    {
        _context = context;
    }

    [ObservableProperty]
    private bool _enableZoomFormat = true;

    [ObservableProperty]
    private int _zoomLevel = 100;

    [ObservableProperty]
    private bool _formatFocusA1 = true;

    [ObservableProperty]
    private bool _formatActiveFirstSheet = true;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingFiles))]
    private ObservableCollection<ExcelFileItem> _pendingFiles = new();

    public bool HasPendingFiles => PendingFiles.Count > 0;

    [RelayCommand]
    private void SelectExcelFallback()
    {
        if (IsProcessing) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel ファイル (*.xlsx;*.xls)|*.xlsx;*.xls|すべてのファイル (*.*)|*.*",
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
                    catch { } // アクセス例外は無視
                }
                else if (File.Exists(path))
                {
                    if (path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
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
        {
            _context?.ReportError("有効な新しい Excel ファイルは追加されませんでした。");
        }
        else
        {
            _context?.ReportSuccess($"{newCount} 個の新しいファイルをスキャンし、キューに追加しました。");
        }
        IsProcessing = false;
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
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    item.Status = "処理中...";
                    _context?.ReportProgress($"処理の進捗: {count}/{files.Count} - {item.FileName}", (double)count / files.Count * 100);
                });

                bool success = FormatSingleExcel(app, item.FilePath);

                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    item.Status = success ? "✅ 完了" : "❌ 失敗";
                });
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                _context?.ReportSuccess($"すべてのキューの実行が完了しました！合計 {files.Count} 個のファイルを処理しました。");
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                _context?.ReportError($"自動化の重大なエラー：{ex.Message}");
            });
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

    private bool FormatSingleExcel(Excel.Application app, string path)
    {
        Excel.Workbook? wb = null;
        try
        {
            wb = app.Workbooks.Open(
                Filename: path,
                UpdateLinks: Type.Missing,
                ReadOnly: false);

            for (int i = 1; i <= wb.Worksheets.Count; i++)
            {
                var currentSheet = (Excel.Worksheet)wb.Worksheets[i];
                Excel.Range? usedRange = null;
                
                if (EnableZoomFormat || FormatFocusA1)
                {
                    currentSheet.Activate();
                    
                    if (EnableZoomFormat && app.ActiveWindow != null)
                    {
                        int safeZoom = Math.Max(10, Math.Min(400, ZoomLevel));
                        app.ActiveWindow.Zoom = safeZoom;
                    }

                    if (FormatFocusA1)
                    {
                        var a1 = currentSheet.Range["A1"];
                        a1.Select();
                        Marshal.ReleaseComObject(a1);
                    }
                }

                // フィルター解除 + 数式を値に固定 + 行列の自動調整
                try
                {
                    if (currentSheet.FilterMode)
                    {
                        try { currentSheet.ShowAllData(); } catch { }
                    }
                    if (currentSheet.AutoFilterMode)
                    {
                        currentSheet.AutoFilterMode = false;
                    }

                    usedRange = currentSheet.UsedRange;
                    if (usedRange != null)
                    {
                        // Value2 を自身に代入して、数式を値へ固定
                        usedRange.Value2 = usedRange.Value2;
                        usedRange.Columns.AutoFit();
                        usedRange.Rows.AutoFit();
                    }
                }
                finally
                {
                    if (usedRange != null)
                    {
                        Marshal.ReleaseComObject(usedRange);
                    }
                }
                Marshal.ReleaseComObject(currentSheet);
            }

            // 外部リンクは常に切断
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

            if (FormatActiveFirstSheet && wb.Sheets.Count > 0)
            {
                var firstSheet = (Excel.Worksheet)wb.Sheets[1];
                firstSheet.Activate();
                Marshal.ReleaseComObject(firstSheet);
            }

            wb.Save();
            return true;
        }
        catch (Exception)
        {
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
