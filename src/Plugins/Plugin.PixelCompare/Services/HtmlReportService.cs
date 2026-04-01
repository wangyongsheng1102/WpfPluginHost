using Plugin.PixelCompare.Models;
using System.Text;
using System.IO;

namespace Plugin.PixelCompare.Services;

public sealed class HtmlReportService
{
    public async Task ExportAsync(
        string reportPath,
        string excelPath,
        string sheetName,
        IReadOnlyList<(int RowIndex, ComparisonResult Result)> results)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"ja\">");
        html.AppendLine("<head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">");
        html.AppendLine("<title>PixelCompare Report</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Arial,sans-serif;margin:0;padding:20px;background:#f5f5f5;}");
        html.AppendLine(".container{max-width:1400px;margin:0 auto;background:#fff;padding:20px;border-radius:8px;}");
        html.AppendLine(".section{margin:28px 0;padding:16px;background:#fafafa;border:1px solid #e0e0e0;border-radius:8px;}");
        html.AppendLine("img{max-width:100%;height:auto;border:1px solid #ddd;}");
        html.AppendLine(".warn{background:#fff3cd;padding:8px;border-radius:4px;}");
        html.AppendLine(".err{background:#f8d7da;padding:8px;border-radius:4px;}");
        html.AppendLine(".ok{background:#d4edda;padding:8px;border-radius:4px;}");
        html.AppendLine("</style></head><body><div class=\"container\">");
        html.AppendLine("<h1>PixelCompare レポート</h1>");
        html.AppendLine($"<p><strong>作成日時:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine($"<p><strong>Excel:</strong> {excelPath}</p>");
        html.AppendLine($"<p><strong>シート:</strong> {sheetName}</p>");

        foreach (var entry in results.OrderBy(x => x.RowIndex))
        {
            var rowIndex = entry.RowIndex;
            var result = entry.Result;
            html.AppendLine($"<div class=\"section\"><h3>{rowIndex} 行目</h3>");

            if (result.IsSizeMismatch)
            {
                html.AppendLine($"<div class=\"warn\">サイズが一致しません: {result.SizeInfo}</div>");
            }
            else if (result.HasError)
            {
                html.AppendLine($"<div class=\"err\">比較に失敗しました: {result.ErrorMessage}</div>");
            }
            else
            {
                html.AppendLine($"<div class=\"ok\">差異領域数: {result.DiffCount} / 差異率: {result.DifferencePercentage:F2}%</div>");
                AppendImageIfExists(html, result.MarkedImage1Path, "左画像（赤枠）");
                AppendImageIfExists(html, result.MarkedImage2Path, "右画像（赤枠）");
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</div></body></html>");
        await File.WriteAllTextAsync(reportPath, html.ToString(), Encoding.UTF8);
    }

    private static void AppendImageIfExists(StringBuilder html, string? path, string title)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var base64 = Convert.ToBase64String(File.ReadAllBytes(path));
        html.AppendLine($"<p><strong>{title}</strong></p>");
        html.AppendLine($"<img src=\"data:image/png;base64,{base64}\" alt=\"{title}\" />");
    }
}
