using System;
using System.IO;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.ReviewCheck.Services;

public sealed class WbsLookupService
{
    public WbsPeople? TryReadPeople(string wbsExcelPath, string? functionId, string? systemName, string? objectName)
    {
        if (string.IsNullOrWhiteSpace(wbsExcelPath) || !File.Exists(wbsExcelPath))
        {
            return null;
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

            var ws = FindWbsSheet(wb);
            if (ws is null)
            {
                return null;
            }
            try
            {
                var used = ws.UsedRange;
                try
                {
                    var rowCount = SafeCount(() => used.Rows.Count);
                    var colCount = SafeCount(() => used.Columns.Count);
                    var maxRow = Math.Min(rowCount, 400);
                    var maxCol = Math.Min(colCount, 60);

                    if (maxRow <= 0 || maxCol <= 0)
                    {
                        return null;
                    }

                    var normalizedSystem = NormalizeSystemForWbs(systemName);

                    for (int r = 8; r <= maxRow; r++)
                    {
                        var rowValues = ReadRow(used, r, maxCol);
                        if (rowValues.Count < 49)
                        {
                            continue;
                        }

                        var rowObject = rowValues[2];
                        var rowSystem = rowValues[3];
                        var rowFunctionId = rowValues[4];

                        if (!EqualsText(rowFunctionId, functionId))
                        {
                            continue;
                        }
                        if (!EqualsText(rowSystem, normalizedSystem))
                        {
                            continue;
                        }
                        if (!EqualsText(rowObject, objectName))
                        {
                            continue;
                        }

                        return new WbsPeople
                        {
                            CodeUserName = rowValues[18],
                            CodeUserDate = rowValues[21],
                            CodeReviewUserName = rowValues[27],
                            CodeReviewUserDate = rowValues[30],
                            TestUserName = rowValues[36],
                            TestUserDate = rowValues[39],
                            TestReviewUserName = rowValues[45],
                            TestReviewUserDate = rowValues[48]
                        };
                    }

                    return null;
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

    private static List<string?> ReadRow(Excel.Range usedRange, int row, int maxCol)
    {
        var values = new List<string?>(maxCol);
        for (int c = 1; c <= maxCol; c++)
        {
            var cell = (Excel.Range)usedRange.Cells[row, c];
            try
            {
                values.Add(Convert.ToString(cell.Value2)?.Trim());
            }
            finally
            {
                Marshal.ReleaseComObject(cell);
            }
        }
        return values;
    }

    private static bool EqualsText(string? left, string? right)
    {
        return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeSystemForWbs(string? systemName)
    {
        return systemName switch
        {
            "EnabilityCis" => "EnabilityCis",
            "EnabilityOrder" => "EnabilityOrder",
            "EnabilityPortal" => "EnabilityPortal",
            "EnabilityPortal2" => "EnabilityPortal2",
            _ => systemName
        };
    }

    private static Excel.Worksheet? FindWbsSheet(Excel.Workbook wb)
    {
        for (int i = 1; i <= wb.Worksheets.Count; i++)
        {
            var ws = (Excel.Worksheet)wb.Worksheets[i];
            try
            {
                if ((ws.Name ?? string.Empty).Contains("WBS", StringComparison.OrdinalIgnoreCase))
                {
                    return ws;
                }
            }
            catch
            {
                // ignore and continue
            }
            Marshal.ReleaseComObject(ws);
        }

        return null;
    }
}
