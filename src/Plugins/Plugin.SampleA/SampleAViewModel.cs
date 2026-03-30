using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.SampleA;

public partial class SampleAViewModel : ObservableObject
{
    [ObservableProperty]
    private string _excelPath = string.Empty;

    [ObservableProperty]
    private DataTable? _excelTable;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    [RelayCommand]
    private void SelectExcel()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls|所有文件 (*.*)|*.*",
            Title = "选择 Excel 文件"
        };
        if (dlg.ShowDialog() != true)
            return;

        ExcelPath = dlg.FileName;
        LoadExcel(dlg.FileName);
    }

    private void LoadExcel(string path)
    {
        StatusMessage = string.Empty;
        Excel.Application? app = null;
        Excel.Workbook? wb = null;
        Excel.Worksheet? ws = null;
        Excel.Range? usedRange = null;
        try
        {
            if (!File.Exists(path))
            {
                StatusMessage = "文件不存在。";
                ExcelTable = null;
                return;
            }

            app = new Excel.Application();
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;

            // 只读打开，避免误写；需本机安装 Excel 并允许 COM 自动化
            wb = app.Workbooks.Open(
                Filename: path,
                UpdateLinks: Type.Missing,
                ReadOnly: true);

            ws = (Excel.Worksheet)wb.Sheets[1];
            usedRange = ws.UsedRange;
            if (usedRange == null)
            {
                ExcelTable = new DataTable();
                return;
            }

            int rowCount = usedRange.Rows.Count;
            int colCount = usedRange.Columns.Count;
            if (rowCount == 0 || colCount == 0)
            {
                ExcelTable = new DataTable();
                return;
            }

            var table = new DataTable();

            for (int c = 1; c <= colCount; c++)
            {
                var headerCell = (Excel.Range)usedRange.Cells[1, c];
                string header;
                try
                {
                    header = headerCell.Text?.ToString()?.Trim() ?? string.Empty;
                }
                finally
                {
                    Marshal.ReleaseComObject(headerCell);
                }

                if (string.IsNullOrEmpty(header))
                    header = $"列{c}";
                var columnName = header;
                var baseName = header;
                int n = 1;
                while (table.Columns.Contains(columnName))
                {
                    columnName = $"{baseName}_{n}";
                    n++;
                }
                table.Columns.Add(columnName, typeof(string));
            }

            for (int r = 2; r <= rowCount; r++)
            {
                var row = table.NewRow();
                for (int c = 1; c <= colCount; c++)
                {
                    var cell = (Excel.Range)usedRange.Cells[r, c];
                    try
                    {
                        row[c - 1] = cell.Text?.ToString() ?? string.Empty;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(cell);
                    }
                }
                table.Rows.Add(row);
            }

            ExcelTable = table;
        }
        catch (COMException ex)
        {
            ExcelTable = null;
            StatusMessage =
                $"Excel 自动化失败（{ex.Message}）。请确认本机已安装 Microsoft Excel，且未被策略禁用 COM 自动化。";
        }
        catch (Exception ex)
        {
            ExcelTable = null;
            StatusMessage = $"无法读取 Excel：{ex.Message}";
        }
        finally
        {
            if (usedRange != null)
                Marshal.ReleaseComObject(usedRange);
            if (ws != null)
                Marshal.ReleaseComObject(ws);
            if (wb != null)
            {
                wb.Close(SaveChanges: false);
                Marshal.ReleaseComObject(wb);
            }
            if (app != null)
            {
                app.Quit();
                Marshal.ReleaseComObject(app);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
