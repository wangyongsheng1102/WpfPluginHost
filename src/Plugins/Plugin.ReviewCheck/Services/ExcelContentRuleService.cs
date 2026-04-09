using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace Plugin.ReviewCheck.Services;

public sealed class ExcelContentRuleService
{
    public ExcelContentCheckResult Check(string workbookPath, string artifactKey, string artifactLabel, WbsPeople? wbsPeople)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
        {
            return new ExcelContentCheckResult(workbookPath, new[]
            {
                new CheckResultItem("content","✕",CheckSeverity.Error,"","", "Excel ファイルが存在しません。")
            });
        }

        var items = new List<CheckResultItem>();

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
                Filename: workbookPath,
                UpdateLinks: Type.Missing,
                ReadOnly: true);

            var sheets = new List<Excel.Worksheet>();
            try
            {
                for (int i = 1; i <= wb.Worksheets.Count; i++)
                {
                    sheets.Add((Excel.Worksheet)wb.Worksheets[i]);
                }

                items.Add(new CheckResultItem("content", "〇", CheckSeverity.Info, "", "", $"対象: {artifactLabel}"));
                items.AddRange(CheckExpectedSheets(sheets, artifactKey));
                items.AddRange(CheckCoverPeople(sheets, artifactKey, wbsPeople));
            }
            finally
            {
                foreach (var s in sheets)
                {
                    Marshal.ReleaseComObject(s);
                }
            }

            return new ExcelContentCheckResult(workbookPath, items);
        }
        catch (Exception ex)
        {
            items.Add(new CheckResultItem("content", "✕", CheckSeverity.Error, "", "", $"Excel 解析に失敗: {ex.Message}"));
            return new ExcelContentCheckResult(workbookPath, items);
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

    private static IReadOnlyList<CheckResultItem> CheckExpectedSheets(IReadOnlyList<Excel.Worksheet> sheets, string artifactKey)
    {
        var items = new List<CheckResultItem>();

        // content_check 的重点 sheet 组（按旧脚本）
        items.Add(ToSheetExistence(sheets, "表紙", "表紙"));
        items.Add(ToSheetExistence(sheets, "成果物一覧", "成果物一覧"));
        items.Add(ToSheetExistence(sheets, "ソース", "ソースファイル"));
        if (artifactKey != "EXCEL_CD_CHECKLIST")
        {
            items.Add(ToSheetExistence(sheets, "作業結果確認", "⑤作業結果確認"));
        }

        return items;
    }

    private static CheckResultItem ToSheetExistence(IReadOnlyList<Excel.Worksheet> sheets, string keyword, string displayName)
    {
        var hit = sheets.Any(s => GetNameSafe(s).Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return new CheckResultItem(
            Step: "content",
            Status: hit ? "〇" : "✕",
            Severity: hit ? CheckSeverity.Info : CheckSeverity.Warning,
            SheetName: displayName,
            CellRef: "",
            Message: hit ? $"Sheet「{displayName}」が見つかりました。" : $"Sheet「{displayName}」が見つかりません（キーワード: {keyword}）。");
    }

    private static IReadOnlyList<CheckResultItem> CheckCoverPeople(IReadOnlyList<Excel.Worksheet> sheets, string artifactKey, WbsPeople? wbsPeople)
    {
        var items = new List<CheckResultItem>();

        var cover = sheets.FirstOrDefault(s => GetNameSafe(s).Contains("表紙", StringComparison.OrdinalIgnoreCase))
                    ?? sheets.FirstOrDefault();

        if (cover is null)
        {
            items.Add(new CheckResultItem("content", "✕", CheckSeverity.Warning, "", "", "Sheet が存在しないため、作成者/確認者チェックをスキップしました。"));
            return items;
        }

        var (authorCell, authorValue) = FindLabelNeighborValue(cover, "作成者");
        var (reviewerCell, reviewerValue) = FindLabelNeighborValue(cover, "確認者");

        items.Add(ToRequiredValueItem("content", GetNameSafe(cover), authorCell, "作成者", authorValue));
        items.Add(ToRequiredValueItem("content", GetNameSafe(cover), reviewerCell, "確認者", reviewerValue));

        var expectedUser = SelectExpectedUser(artifactKey, wbsPeople);
        var expectedDate = SelectExpectedDate(artifactKey, wbsPeople);

        if (!string.IsNullOrWhiteSpace(expectedUser))
        {
            items.Add(ToEqualsItem("content", GetNameSafe(cover), authorCell, "作成者(WBS)", expectedUser!, authorValue));
        }

        if (!string.IsNullOrWhiteSpace(expectedDate))
        {
            var (dateCell, dateValue) = FindLabelNeighborValue(cover, "作成日");
            items.Add(ToEqualsItem("content", GetNameSafe(cover), dateCell, "作成日(WBS)", expectedDate!, dateValue));
        }

        return items;
    }

    private static CheckResultItem ToRequiredValueItem(string step, string sheetName, string? cellRef, string label, string? actual)
    {
        var ok = !string.IsNullOrWhiteSpace(actual);
        return new CheckResultItem(
            Step: step,
            Status: ok ? "〇" : "✕",
            Severity: ok ? CheckSeverity.Info : CheckSeverity.Error,
            SheetName: sheetName,
            CellRef: cellRef ?? "",
            Message: ok ? $"{label}: {actual}" : $"{label} が未記入です。");
    }

    private static CheckResultItem ToEqualsItem(string step, string sheetName, string? cellRef, string label, string expected, string? actual)
    {
        var ok = string.Equals(Normalize(expected), Normalize(actual), StringComparison.OrdinalIgnoreCase);
        return new CheckResultItem(
            Step: step,
            Status: ok ? "〇" : "✕",
            Severity: ok ? CheckSeverity.Info : CheckSeverity.Warning,
            SheetName: sheetName,
            CellRef: cellRef ?? "",
            Message: ok ? $"{label}: 一致（{expected}）" : $"{label}: 不一致（期待={expected}, 実際={actual ?? ""}）");
    }

    private static (string? CellRef, string? Value) FindLabelNeighborValue(Excel.Worksheet sheet, string label)
    {
        Excel.Range? used = null;
        try
        {
            used = sheet.UsedRange;
            var rowCount = SafeCount(() => used.Rows.Count);
            var colCount = SafeCount(() => used.Columns.Count);
            var maxRow = Math.Min(rowCount, 80);
            var maxCol = Math.Min(colCount, 30);

            if (maxRow <= 0 || maxCol <= 0)
            {
                return (null, null);
            }

            for (int r = 1; r <= maxRow; r++)
            {
                for (int c = 1; c <= maxCol; c++)
                {
                    var cell = (Excel.Range)used.Cells[r, c];
                    var text = Convert.ToString(cell.Value2)?.Trim();
                    var addr = Convert.ToString(cell.Address);
                    Marshal.ReleaseComObject(cell);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var nonNullText = text!;

                    if (!nonNullText.Contains(label, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var neighborValue = ReadCellValue(used, r, c + 1);
                    return (addr, neighborValue);
                }
            }

            return (null, null);
        }
        finally
        {
            if (used != null)
            {
                Marshal.ReleaseComObject(used);
            }
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

    private static string? ReadCellValue(Excel.Range usedRange, int row, int col)
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

    private static string GetNameSafe(Excel.Worksheet sheet)
    {
        try
        {
            return sheet.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? SelectExpectedUser(string artifactKey, WbsPeople? people)
    {
        if (people is null)
        {
            return null;
        }

        return artifactKey is "EXCEL_LIST" or "EXCEL_COMPARE" or "EXCEL_CD_CHECKLIST"
            ? people.CodeUserName
            : people.TestUserName;
    }

    private static string? SelectExpectedDate(string artifactKey, WbsPeople? people)
    {
        if (people is null)
        {
            return null;
        }

        return artifactKey is "EXCEL_LIST" or "EXCEL_COMPARE" or "EXCEL_CD_CHECKLIST"
            ? people.CodeUserDate
            : people.TestUserDate;
    }
}
