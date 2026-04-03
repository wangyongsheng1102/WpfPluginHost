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
            try
            {
                _ = GetColumnIndex(leftColumnName);
                _ = GetColumnIndex(rightColumnName);
            }
            catch
            {
                return Array.Empty<string>();
            }

            foreach (var worksheet in package.Workbook.Worksheets)
            {
                var drawings = IndexDrawings(worksheet);
                var rowCount = GetActualLastRow(worksheet);
                var hasAnyRowWithPictures = false;

                for (var row = 2; row <= rowCount; row++)
                {
                    var pics = GetPicturesInRow(drawings, row);
                    if (pics.Count == 0)
                    {
                        continue;
                    }

                    hasAnyRowWithPictures = true;
                    break;
                }

                if (hasAnyRowWithPictures)
                {
                    matchedSheets.Add(worksheet.Name);
                }
            }

            return matchedSheets;
        });
    }

    public Task<IReadOnlyList<PixelCompareRowExtract>> ExtractRowsAsync(
        string excelPath,
        string sheetName,
        string leftColumnName,
        string rightColumnName)
    {
        return Task.Run<IReadOnlyList<PixelCompareRowExtract>>(() =>
        {
            if (!File.Exists(excelPath))
            {
                return Array.Empty<PixelCompareRowExtract>();
            }

            using var package = new ExcelPackage(new FileInfo(excelPath));
            var worksheet = package.Workbook.Worksheets[sheetName];
            if (worksheet is null)
            {
                return Array.Empty<PixelCompareRowExtract>();
            }

            var leftColumnIndex = GetColumnIndex(leftColumnName);
            var rightColumnIndex = GetColumnIndex(rightColumnName);
            var leftLetters = NormalizeColumnLetters(leftColumnName);
            var rightLetters = NormalizeColumnLetters(rightColumnName);

            if (leftColumnIndex == rightColumnIndex)
            {
                throw new ArgumentException("左列と右列に同じ列を指定することはできません。", nameof(rightColumnName));
            }

            var rowCount = GetActualLastRow(worksheet);
            var drawings = IndexDrawings(worksheet);
            var list = new List<PixelCompareRowExtract>();

            for (var row = 2; row <= rowCount; row++)
            {
                var built = TryBuildRowExtract(
                    drawings,
                    row,
                    leftColumnIndex,
                    rightColumnIndex,
                    leftLetters,
                    rightLetters,
                    sheetName);
                if (built is not null)
                {
                    list.Add(built);
                }
            }

            return list;
        });
    }

    private static PixelCompareRowExtract? TryBuildRowExtract(
        Dictionary<(int row, int col), ExcelPicture> drawings,
        int row,
        int leftCol,
        int rightCol,
        string leftLetters,
        string rightLetters,
        string sheetName)
    {
        var pics = GetPicturesInRow(drawings, row);
        if (pics.Count == 0)
        {
            return null;
        }

        if (pics.Count == 1)
        {
            var (col, pic) = pics[0];
            var path = SavePictureToTempFile(pic, row, "single");
            string msg;
            var img1 = string.Empty;
            var img2 = string.Empty;
            if (col == leftCol)
            {
                msg = $"{rightLetters}列の画像が欠けています（{leftLetters}列にのみ画像があります）";
                img1 = path;
            }
            else if (col == rightCol)
            {
                msg = $"{leftLetters}列の画像が欠けています（{rightLetters}列にのみ画像があります）";
                img2 = path;
            }
            else
            {
                var x = ToColumnLetters(col);
                msg = $"{leftLetters}列・{rightLetters}列に画像がありません（{x}列にのみ画像があります）";
                img1 = path;
            }

            return new PixelCompareRowExtract
            {
                SheetName = sheetName,
                RowIndex = row,
                IsPixelComparable = false,
                ValidationMessageJa = msg,
                Image1Path = img1,
                Image2Path = img2
            };
        }

        var cols = pics.Select(p => p.col).Distinct().OrderBy(c => c).ToList();
        var hasLeft = cols.Contains(leftCol);
        var hasRight = cols.Contains(rightCol);
        var extraCols = cols.Where(c => c != leftCol && c != rightCol).OrderBy(c => c).ToList();
        var onlyLeftAndRight = cols.All(c => c == leftCol || c == rightCol);
        var validPair = onlyLeftAndRight && hasLeft && hasRight && cols.Count == 2;

        if (validPair)
        {
            var leftPic = pics.First(p => p.col == leftCol).pic;
            var rightPic = pics.First(p => p.col == rightCol).pic;
            var leftPath = SavePictureToTempFile(leftPic, row, "c1");
            var rightPath = SavePictureToTempFile(rightPic, row, "c2");
            return new PixelCompareRowExtract
            {
                SheetName = sheetName,
                RowIndex = row,
                IsPixelComparable = true,
                ValidationMessageJa = null,
                Image1Path = leftPath,
                Image2Path = rightPath
            };
        }

        string invalidMsg;
        if (extraCols.Count > 0)
        {
            var extras = string.Join("、", extraCols.Select(c => $"{ToColumnLetters(c)}列"));
            invalidMsg = $"{leftLetters}列・{rightLetters}列の範囲外に不正なスクリーンショットがあります（{extras}）";
        }
        else
        {
            var detected = string.Join("、", cols.Select(c => $"{ToColumnLetters(c)}列"));
            invalidMsg = $"{leftLetters}列・{rightLetters}列の画像が揃っていません（検出列: {detected}）";
        }

        string path1 = string.Empty;
        string path2 = string.Empty;
        if (hasLeft)
        {
            path1 = SavePictureToTempFile(pics.First(p => p.col == leftCol).pic, row, "c1");
        }

        if (hasRight)
        {
            path2 = SavePictureToTempFile(pics.First(p => p.col == rightCol).pic, row, "c2");
        }

        if (string.IsNullOrEmpty(path1) && string.IsNullOrEmpty(path2) && pics.Count > 0)
        {
            path1 = SavePictureToTempFile(pics[0].pic, row, "c1");
            if (pics.Count > 1)
            {
                path2 = SavePictureToTempFile(pics[1].pic, row, "c2");
            }
        }

        return new PixelCompareRowExtract
        {
            SheetName = sheetName,
            RowIndex = row,
            IsPixelComparable = false,
            ValidationMessageJa = invalidMsg,
            Image1Path = path1,
            Image2Path = path2
        };
    }

    private static List<(int col, ExcelPicture pic)> GetPicturesInRow(
        Dictionary<(int row, int col), ExcelPicture> drawings,
        int row)
    {
        var list = new List<(int col, ExcelPicture pic)>();
        foreach (var kv in drawings)
        {
            if (kv.Key.row == row)
            {
                list.Add((kv.Key.col, kv.Value));
            }
        }

        list.Sort((a, b) => a.col.CompareTo(b.col));
        return list;
    }

    private static Dictionary<(int row, int col), ExcelPicture> IndexDrawings(ExcelWorksheet worksheet)
    {
        var index = new Dictionary<(int row, int col), ExcelPicture>();
        foreach (var drawing in worksheet.Drawings)
        {
            if (drawing is ExcelPicture pic)
            {
                var row = pic.From.Row + 1;
                var col = pic.From.Column + 1;
                index.TryAdd((row, col), pic);
            }
        }

        return index;
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

    private static string NormalizeColumnLetters(string columnName)
    {
        return (columnName ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>1 始まりの列番号を Excel 列記号に変換する（1→A、27→AA）。</summary>
    private static string ToColumnLetters(int columnIndex)
    {
        if (columnIndex < 1)
        {
            return "?";
        }

        var stack = new Stack<char>();
        var n = columnIndex;
        while (n > 0)
        {
            n--;
            stack.Push((char)('A' + n % 26));
            n /= 26;
        }

        return new string(stack.ToArray());
    }
}
