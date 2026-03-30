using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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
    private string _status = "等待处理";
}

public partial class SampleAViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = string.Empty;

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

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    [RelayCommand]
    private void SelectExcelFallback()
    {
        if (IsProcessing) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls|所有文件 (*.*)|*.*",
            Title = "手动选择 Excel 文件",
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
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ExecuteFormatAsync()
    {
        if (IsProcessing || !HasPendingFiles) return;

        IsProcessing = true;
        StatusMessage = "开始批量格式化...";
        
        await Task.Run(() => ProcessFiles(PendingFiles.ToList()));
        IsProcessing = false;
    }

    public async Task HandleDroppedPathsAsync(string[] paths)
    {
        if (IsProcessing) return;
        IsProcessing = true;
        StatusMessage = "扫描文件中...";

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
                    catch { } // Ignore access exceptions
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
            StatusMessage = "未添加有效的新 Excel 文件。";
        }
        else
        {
            StatusMessage = $"成功扫描并添加了 {newCount} 个新文件至队列待处理。";
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
                    item.Status = "处理中...";
                    StatusMessage = $"处理进度: {count}/{files.Count} - {item.FileName}";
                });

                bool success = FormatSingleExcel(app, item.FilePath);

                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    item.Status = success ? "✅ 成功" : "❌ 失败";
                });
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                StatusMessage = $"全部队列执行完成！共处理了 {files.Count} 个文件。";
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                StatusMessage = $"自动化严重异常：{ex.Message}";
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
                Marshal.ReleaseComObject(currentSheet);
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
