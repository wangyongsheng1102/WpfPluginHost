using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.SampleA;

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

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public async void HandleDroppedPathsAsync(string[] paths)
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

        if (excelFiles.Count == 0)
        {
            StatusMessage = "未发现有效的 Excel 文件。";
            IsProcessing = false;
            return;
        }

        StatusMessage = $"找到 {excelFiles.Count} 个文件，即将开始处理...";
        
        await Task.Run(() => ProcessFiles(excelFiles));
        IsProcessing = false;
    }

    private void ProcessFiles(List<string> files)
    {
        Excel.Application? app = null;
        try
        {
            app = new Excel.Application();
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;

            int count = 0;
            foreach (var path in files)
            {
                count++;
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    StatusMessage = $"处理中 ({count}/{files.Count}): {Path.GetFileName(path)}";
                });

                FormatSingleExcel(app, path);
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                StatusMessage = $"格式化完成！共处理了 {files.Count} 个文件。";
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                StatusMessage = $"自动化异常：{ex.Message}";
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

    private void FormatSingleExcel(Excel.Application app, string path)
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
        }
        catch (Exception)
        {
            // Silently ignore individual file errors to continue batch processing
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
