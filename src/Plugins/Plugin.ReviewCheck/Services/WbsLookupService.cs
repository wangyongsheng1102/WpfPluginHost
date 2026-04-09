using System;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.ReviewCheck.Services;

public sealed class WbsLookupService
{
    public WbsPeople TryReadPeople(string wbsExcelPath)
    {
        if (string.IsNullOrWhiteSpace(wbsExcelPath) || !File.Exists(wbsExcelPath))
        {
            return new WbsPeople(null, null);
        }

        Excel.Application? app = null;
        Excel.Workbook? wb = null;
        try
        {
            app = new Excel.Application
            {
                Visible = false,
                DisplayAlerts = false,
                ScreenUpdating = false
            };

            wb = app.Workbooks.Open(
                Filename: wbsExcelPath,
                UpdateLinks: Type.Missing,
                ReadOnly: true);

            var ws = (Excel.Worksheet)wb.Worksheets[1];
            try
            {
                var used = ws.UsedRange;
                try
                {
                    var rowCount = SafeCount(() => used.Rows.Count);
                    var colCount = SafeCount(() => used.Columns.Count);
                    var maxRow = Math.Min(rowCount, 50);
                    var maxCol = Math.Min(colCount, 20);

                    if (maxRow <= 0 || maxCol <= 0)
                    {
                        return new WbsPeople(null, null);
                    }

                    string? author = null;
                    string? reviewer = null;

                    for (int r = 1; r <= maxRow; r++)
                    {
                        for (int c = 1; c <= maxCol; c++)
                        {
                            var cell = (Excel.Range)used.Cells[r, c];
                            var text = Convert.ToString(cell.Value2)?.Trim();
                            Marshal.ReleaseComObject(cell);

                            if (string.IsNullOrWhiteSpace(text))
                            {
                                continue;
                            }

                            var nonNullText = text!;

                            if (author is null && nonNullText.Contains("作成者", StringComparison.OrdinalIgnoreCase))
                            {
                                author = ReadNeighborValue(used, r, c + 1);
                            }

                            if (reviewer is null && (nonNullText.Contains("確認者", StringComparison.OrdinalIgnoreCase) || nonNullText.Contains("レビュア", StringComparison.OrdinalIgnoreCase)))
                            {
                                reviewer = ReadNeighborValue(used, r, c + 1);
                            }

                            if (author is not null && reviewer is not null)
                            {
                                return new WbsPeople(author, reviewer);
                            }
                        }
                    }

                    return new WbsPeople(author, reviewer);
                }
                finally
                {
                    Marshal.ReleaseComObject(used);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ws);
            }
        }
        finally
        {
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

    private static int SafeCount(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }

    private static string? ReadNeighborValue(Excel.Range usedRange, int row, int col)
    {
        try
        {
            var cell = (Excel.Range)usedRange.Cells[row, col];
            try
            {
                return Convert.ToString(cell.Value2)?.Trim();
            }
            finally
            {
                Marshal.ReleaseComObject(cell);
            }
        }
        catch
        {
            return null;
        }
    }
}
