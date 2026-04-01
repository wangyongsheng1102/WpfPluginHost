using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Npgsql;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

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
        worksheet.Cell(1, 1).Style.Font.FontSize = 13;
        worksheet.Cell(1, 1).Style.Font.SetFontName("MS PGothic");

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        string? database = builder.ContainsKey("username") ? builder["username"]?.ToString() : null;

        worksheet.Cell(3, 1).Value = database?.Split("_").FirstOrDefault() ?? "データベース名取得失敗";
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
                tableComment = _databaseService.GetTableCommentAsync(connectionString, schemaName, nameOnly).Result;
                columnComments = _databaseService.GetColumnCommentsAsync(connectionString, schemaName, nameOnly).Result;
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
            int headerStartCol = 3;

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

            var newResultMapForOldBefore = newResults.ToDictionary(r =>
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));

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

            foreach (var newResult in newResults)
            {
                string pkKey = string.Join("|", newResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                if (oldBeforeRowMap.ContainsKey(pkKey))
                    continue;

                worksheet.Cell(currentRow, 2).Value = "";

                oldBeforeRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    worksheet.Cell(currentRow, dataCol).Value = "";
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

            var newResultMapForOldAfter = newResults.ToDictionary(r =>
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));

            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                string statusLabel = GetAfterLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;

                oldAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = GetAfterValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            foreach (var newResult in newResults)
            {
                string pkKey = string.Join("|", newResult.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

                if (oldAfterRowMap.ContainsKey(pkKey))
                    continue;

                worksheet.Cell(currentRow, 2).Value = GetAfterLabel(newResult.Status);

                oldAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = GetAfterValue(newResult, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow += 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        workbook.SaveAs(filePath);
    }

    private static void ApplyCellBorder(IXLCell cell)
    {
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static string GetBeforeLabel(ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.Deleted => "削除前",
            ComparisonStatus.Added => "追加前",
            ComparisonStatus.Updated => "更新前",
            _ => ""
        };
    }

    private static string GetAfterLabel(ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.Deleted => "削除後",
            ComparisonStatus.Added => "追加後",
            ComparisonStatus.Updated => "更新後",
            _ => ""
        };
    }

    private static object? GetBaseValue(RowComparisonResult result, string column)
    {
        if (result.OldValues.TryGetValue(column, out var value))
        {
            return value;
        }
        if (result.PrimaryKeyValues.TryGetValue(column, out var pk))
        {
            return pk;
        }
        return null;
    }

    private static object? GetAfterValue(RowComparisonResult result, string column)
    {
        if (result.NewValues.TryGetValue(column, out var value))
        {
            return value;
        }
        if (result.PrimaryKeyValues.TryGetValue(column, out var pk))
        {
            return pk;
        }
        return null;
    }
}

