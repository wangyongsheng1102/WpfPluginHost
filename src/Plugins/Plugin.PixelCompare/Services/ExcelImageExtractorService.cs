using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using Plugin.PixelCompare.Models;
using System.IO;

namespace Plugin.PixelCompare.Services;

public sealed class ExcelImageExtractorService
{
    static ExcelImageExtractorService()
    {
        ExcelPackage.License.SetNonCommercialPersonal("PixelCompare");
    }

    public Task<IReadOnlyList<string>> GetComparableSheetNamesAsync(string excelPath, string leftColumnName, string rightColumnName)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (!File.Exists(excelPath))
            {
                return Array.Empty<string>();
            }

            using var package = new ExcelPackage(new FileInfo(excelPath));
            var matchedSheets = new List<string>();

            foreach (var worksheet in package.Workbook.Worksheets)
            {
                var drawings = IndexDrawings(worksheet);
                var leftColumnIndex = GetColumnIndex(leftColumnName);
                var rightColumnIndex = GetColumnIndex(rightColumnName);
                var rowCount = GetActualLastRow(worksheet);

                var hasPair = false;
                for (var row = 2; row <= rowCount; row++)
                {
                    var left = GetPictureAtCell(drawings, row, leftColumnIndex);
                    var right = GetPictureAtCell(drawings, row, rightColumnIndex);
                    if (left != null && right != null)
                    {
                        hasPair = true;
                        break;
                    }
                }

                if (hasPair)
                {
                    matchedSheets.Add(worksheet.Name);
                }
            }

            return matchedSheets;
        });
    }

    public Task<IReadOnlyList<ExtractedImagePair>> ExtractPairsAsync(string excelPath, string sheetName, string leftColumnName, string rightColumnName)
    {
        return Task.Run<IReadOnlyList<ExtractedImagePair>>(() =>
        {
            if (!File.Exists(excelPath))
            {
                return Array.Empty<ExtractedImagePair>();
            }

            using var package = new ExcelPackage(new FileInfo(excelPath));
            var worksheet = package.Workbook.Worksheets[sheetName];
            if (worksheet is null)
            {
                return Array.Empty<ExtractedImagePair>();
            }

            var leftColumnIndex = GetColumnIndex(leftColumnName);
            var rightColumnIndex = GetColumnIndex(rightColumnName);
            var rowCount = GetActualLastRow(worksheet);
            var drawings = IndexDrawings(worksheet);
            var list = new List<ExtractedImagePair>();

            for (var row = 2; row <= rowCount; row++)
            {
                var left = GetPictureAtCell(drawings, row, leftColumnIndex);
                var right = GetPictureAtCell(drawings, row, rightColumnIndex);
                if (left is null || right is null)
                {
                    continue;
                }

                var leftPath = SavePictureToTempFile(left, row, "c1");
                var rightPath = SavePictureToTempFile(right, row, "c2");
                list.Add(new ExtractedImagePair
                {
                    SheetName = sheetName,
                    RowIndex = row,
                    Image1Path = leftPath,
                    Image2Path = rightPath
                });
            }

            return list;
        });
    }

    private static Dictionary<(int row, int col), ExcelPicture> IndexDrawings(ExcelWorksheet worksheet)
    {
        var index = new Dictionary<(int row, int col), ExcelPicture>();
        foreach (var drawing in worksheet.Drawings)
        {
            if (drawing is ExcelPicture pic)
            {
                // pic.From は 0-indexed なので 1-indexed に合わせてキーにする
                var row = pic.From.Row + 1;
                var col = pic.From.Column + 1;
                index.TryAdd((row, col), pic);
            }
        }
        return index;
    }

    private static ExcelPicture? GetPictureAtCell(Dictionary<(int row, int col), ExcelPicture> drawings, int rowIndex, int columnIndex)
    {
        return drawings.TryGetValue((rowIndex, columnIndex), out var pic) ? pic : null;
    }

    private static string SavePictureToTempFile(ExcelPicture picture, int rowIndex, string suffix)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PixelCompareSuite", "excel_images");
        Directory.CreateDirectory(tempDir);

        var filePath = Path.Combine(tempDir, $"row{rowIndex}_{suffix}_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(filePath, picture.Image.ImageBytes);
        return filePath;
    }

    private static int GetActualLastRow(ExcelWorksheet worksheet)
    {
        var lastRowFromCells = worksheet.Dimension?.End.Row ?? 0;
        var lastRowFromPictures = 0;
        foreach (var drawing in worksheet.Drawings)
        {
            if (drawing is ExcelPicture pic)
            {
                lastRowFromPictures = Math.Max(lastRowFromPictures, Math.Max(pic.From.Row, pic.To.Row) + 1);
            }
        }

        return Math.Max(lastRowFromCells, lastRowFromPictures);
    }

    private static int GetColumnIndex(string columnName)
    {
        var normalized = (columnName ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("列名を入力してください。", nameof(columnName));
        }

        var index = 0;
        foreach (var c in normalized)
        {
            if (c < 'A' || c > 'Z')
            {
                throw new ArgumentException($"列名の形式が不正です: {columnName}", nameof(columnName));
            }

            index = index * 26 + (c - 'A' + 1);
        }

        return index;
    }
}
