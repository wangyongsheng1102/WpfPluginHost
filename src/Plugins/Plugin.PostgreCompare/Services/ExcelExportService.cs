using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// 比較結果 Excel 出力（WslPostgreTool ExcelExportService と同一構成）
/// </summary>
public class ExcelExportService
{
    private readonly DatabaseService _databaseService = new();

    public void ExportComparisonResults(
        string filePath,
        List<RowComparisonResult> baseVsOldResults,
        List<RowComparisonResult> baseVsNewResults,
        string connectionString)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("データ比較");

        worksheet.Cell(1, 1).Value = "[自動採番]、[登録/更新/削除日時]、[登録/更新/削除者]、[登録/更新/削除機能]のデータ比較結果が「FALSE」の場合、補足説明が必要がない。";
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.Red;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 11;
        worksheet.Cell(1, 1).Style.Font.SetFontName("MS PGothic");

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        string? database = builder.ContainsKey("username") ? builder["username"]?.ToString() : null;

        worksheet.Cell(3, 1).Value = database != null && database.Split("_").Length > 1
            ? database.Split("_")[0]
            : "データベース名取得失敗";
        worksheet.Cell(3, 1).Style.Font.Bold = true;

        int currentRow = 5;

        var allTableNames = baseVsOldResults.Select(r => r.TableName)
            .Union(baseVsNewResults.Select(r => r.TableName))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        foreach (var tableName in allTableNames)
        {
            var oldResults = baseVsOldResults.Where(r => r.TableName == tableName).ToList();
            var newResults = baseVsNewResults.Where(r => r.TableName == tableName).ToList();

            var parts = tableName.Split('.');
            string schemaName = parts.Length >= 2 ? parts[0] : "public";
            string nameOnly = parts.Length >= 2 ? string.Join(".", parts.Skip(1)) : tableName;

            string tableComment = nameOnly;
            Dictionary<string, string> columnComments = new();

            try
            {
                tableComment = _databaseService.GetTableCommentAsync(connectionString, schemaName, nameOnly).GetAwaiter().GetResult();
                columnComments = _databaseService.GetColumnCommentsAsync(connectionString, schemaName, nameOnly).GetAwaiter().GetResult();
            }
            catch
            {
                tableComment = nameOnly;
            }

            var allColumns = new HashSet<string>();
            foreach (var result in oldResults.Concat(newResults))
            {
                foreach (var key in result.PrimaryKeyValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.OldValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.NewValues.Keys)
                    allColumns.Add(key);
            }

            var columns = allColumns;
            const int headerStartCol = 3;

            // ========== 現行システム ==========
            worksheet.Cell(currentRow, 2).Value = "現行システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            currentRow++;

            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            int colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";

            int colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            var oldBeforeRowMap = new Dictionary<string, int>();
            var oldAfterRowMap = new Dictionary<string, int>();

            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                string statusLabel = GetBeforeLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;

                oldBeforeRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = result.Status == ComparisonStatus.Added ? null : GetBaseValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow++;

            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                string statusLabel = GetAfterLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;

                oldAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = result.Status == ComparisonStatus.Deleted ? null : GetOldValue(result, column);
                    var cell = worksheet.Cell(currentRow, dataCol);
                    cell.Value = value?.ToString() ?? "";
                    ApplyCellBorder(cell);

                    if (result.Status == ComparisonStatus.Updated && oldBeforeRowMap.TryGetValue(pkKey, out _))
                    {
                        object? beforeVal = GetBaseValue(result, column);
                        string beforeStr = beforeVal?.ToString() ?? "";
                        string afterStr = value?.ToString() ?? "";
                        if (beforeStr != afterStr)
                            cell.Style.Fill.BackgroundColor = XLColor.Yellow;
                    }

                    dataCol++;
                }

                currentRow++;
            }

            currentRow += 2;

            // ========== 新システム ==========
            worksheet.Cell(currentRow, 2).Value = "新システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            currentRow++;

            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            var newBeforeRowMap = new Dictionary<string, int>();
            var newAfterRowMap = new Dictionary<string, int>();

            foreach (var newResult in newResults)
            {
                string pkKey = string.Join("|", newResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                string statusLabel = GetBeforeLabel(newResult.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;

                newBeforeRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = newResult.Status == ComparisonStatus.Added ? null : GetBaseValue(newResult, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow++;

            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            foreach (var newResult in newResults)
            {
                string pkKey = string.Join("|", newResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                string statusLabelAfter = GetAfterLabel(newResult.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabelAfter;

                newAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = newResult.Status == ComparisonStatus.Deleted ? null : GetNewValue(newResult, column);
                    var cell = worksheet.Cell(currentRow, dataCol);
                    cell.Value = value?.ToString() ?? "";
                    ApplyCellBorder(cell);

                    if (newResult.Status == ComparisonStatus.Updated && newBeforeRowMap.ContainsKey(pkKey))
                    {
                        object? beforeVal = GetBaseValue(newResult, column);
                        string beforeStr = beforeVal?.ToString() ?? "";
                        string afterStr = value?.ToString() ?? "";
                        if (beforeStr != afterStr)
                            cell.Style.Fill.BackgroundColor = XLColor.Yellow;
                    }

                    dataCol++;
                }

                currentRow++;
            }

            currentRow += 2;

            // ========== 比較結果 ==========
            worksheet.Cell(currentRow, 2).Value = "比較結果";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            currentRow++;

            worksheet.Cell(currentRow, 2).Value = "";
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(currentRow, colIndex).Value = columnComment;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            var oldResultMap = oldResults.ToDictionary(r =>
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
            var newResultMap = newResults.ToDictionary(r =>
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));

            foreach (var oldResult in oldResults)
            {
                string pkKey = string.Join("|", oldResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                bool hasOld = oldAfterRowMap.TryGetValue(pkKey, out int oldRow);
                bool hasNew = newAfterRowMap.TryGetValue(pkKey, out int newRow);

                ComparisonStatus? oldStatus = null;
                ComparisonStatus? newStatus = null;

                if (hasOld && oldResultMap.TryGetValue(pkKey, out var oldResultForStatus))
                    oldStatus = oldResultForStatus.Status;
                if (hasNew && newResultMap.TryGetValue(pkKey, out var newResultForStatus))
                    newStatus = newResultForStatus.Status;

                worksheet.Cell(currentRow, 2).Value = BuildCompareLabel(oldStatus, newStatus);

                colIndex = headerStartCol;
                foreach (var column in columns)
                {
                    if (!hasOld || !hasNew)
                    {
                        worksheet.Cell(currentRow, colIndex).Value = "FALSE";
                        worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.Yellow;
                        ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                    }
                    else
                    {
                        string oldCellRef = GetCellReference(oldRow, colIndex);
                        string newCellRef = GetCellReference(newRow, colIndex);
                        string formula = $"=EXACT({oldCellRef},{newCellRef})";

                        var cell = worksheet.Cell(currentRow, colIndex);
                        cell.SetFormulaA1(formula);
                        ApplyCellBorder(cell);

                        var conditionalFormat = cell.AddConditionalFormat();
                        var currentCellRef = GetCellReference(currentRow, colIndex);
                        conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);
                    }

                    colIndex++;
                }

                currentRow++;
            }

            foreach (var newResult in newResults)
            {
                string pkKey = string.Join("|", newResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                if (oldAfterRowMap.ContainsKey(pkKey))
                    continue;

                bool hasOld = oldAfterRowMap.TryGetValue(pkKey, out int oldRow);
                bool hasNew = newAfterRowMap.TryGetValue(pkKey, out int newRow);

                ComparisonStatus? oldStatus = null;
                ComparisonStatus? newStatus = null;

                if (hasOld && oldResultMap.TryGetValue(pkKey, out var oldResultForStatus))
                    oldStatus = oldResultForStatus.Status;
                if (hasNew && newResultMap.TryGetValue(pkKey, out var newResultForStatus))
                    newStatus = newResultForStatus.Status;

                worksheet.Cell(currentRow, 2).Value = BuildCompareLabel(oldStatus, newStatus);

                colIndex = headerStartCol;
                foreach (var column in columns)
                {
                    string oldCellRef = hasOld ? GetCellReference(oldRow, colIndex) : GetCellReference(newRow, colIndex);
                    string newCellRef = GetCellReference(newRow, colIndex);
                    string formula = $"=EXACT({oldCellRef},{newCellRef})";

                    var cell = worksheet.Cell(currentRow, colIndex);
                    cell.SetFormulaA1(formula);
                    ApplyCellBorder(cell);

                    var conditionalFormat = cell.AddConditionalFormat();
                    var currentCellRef = GetCellReference(currentRow, colIndex);
                    conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);

                    colIndex++;
                }

                currentRow++;
            }

            currentRow += 2;
        }

        worksheet.Style.Font.SetFontName("ＭＳ ゴシック");

        worksheet.Columns().AdjustToContents();
        worksheet.Column("A").Width = 8.38;
        worksheet.Column("B").Width = 30;

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        workbook.SaveAs(filePath);
    }

    private static object? GetBaseValue(RowComparisonResult result, string column)
    {
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
            return pkValue;
        if (result.OldValues.TryGetValue(column, out var oldValue))
            return oldValue;
        return null;
    }

    private static object? GetOldValue(RowComparisonResult result, string column)
    {
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
            return pkValue;
        if (result.NewValues.TryGetValue(column, out var newValue))
            return newValue;
        return null;
    }

    private static object? GetNewValue(RowComparisonResult result, string column)
    {
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
            return pkValue;
        if (result.NewValues.TryGetValue(column, out var newValue))
            return newValue;
        return null;
    }

    private static string GetBeforeLabel(ComparisonStatus status) =>
        status switch
        {
            ComparisonStatus.Deleted => "削除前",
            ComparisonStatus.Added => "登録前",
            ComparisonStatus.Updated => "更新前",
            _ => "更新前"
        };

    private static string GetAfterLabel(ComparisonStatus status) =>
        status switch
        {
            ComparisonStatus.Deleted => "削除後",
            ComparisonStatus.Added => "登録後",
            ComparisonStatus.Updated => "更新後",
            _ => "更新後"
        };

    private static string BuildCompareLabel(ComparisonStatus? oldStatus, ComparisonStatus? newStatus)
    {
        string oldText = oldStatus.HasValue ? $"現行{GetAfterLabel(oldStatus.Value)}" : "現行(該当なし)";
        string newText = newStatus.HasValue ? $"新{GetAfterLabel(newStatus.Value)}" : "新(該当なし)";
        return $"{oldText} / {newText}";
    }

    private static void ApplyCellBorder(IXLCell cell)
    {
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        cell.Style.Font.SetFontName("MS PGothic");
    }

    private static string GetCellReference(int row, int column)
    {
        string columnName = "";
        int col = column;
        while (col > 0)
        {
            col--;
            columnName = (char)('A' + (col % 26)) + columnName;
            col /= 26;
        }

        return $"{columnName}{row}";
    }
}
