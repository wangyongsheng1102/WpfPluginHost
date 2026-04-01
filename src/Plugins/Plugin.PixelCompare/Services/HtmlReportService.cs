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
        var lightFontBase64 = TryLoadFontBase64("LXGWWenKai-Light.ttf");
        var boldFontBase64 = TryLoadFontBase64("LXGWWenKai-Bold.ttf");

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"ja\">");
        html.AppendLine("<head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">");
        html.AppendLine("<title>PixelCompare Report</title>");
        html.AppendLine("<style>");
        if (!string.IsNullOrWhiteSpace(lightFontBase64))
        {
            html.AppendLine($"@font-face{{font-family:'AppReportFont';src:url(data:font/ttf;base64,{lightFontBase64}) format('truetype');font-weight:400;font-style:normal;}}");
        }
        if (!string.IsNullOrWhiteSpace(boldFontBase64))
        {
            html.AppendLine($"@font-face{{font-family:'AppReportFont';src:url(data:font/ttf;base64,{boldFontBase64}) format('truetype');font-weight:700;font-style:normal;}}");
        }
        html.AppendLine("html{scroll-behavior:smooth;}");
        html.AppendLine("body{font-family:'AppReportFont','LXGW WenKai','Segoe UI Emoji',sans-serif;margin:0;padding:20px;background:#f5f5f5;}");
        html.AppendLine(".container{max-width:1400px;margin:0 auto;background:#fff;padding:20px;border-radius:8px;}");
        html.AppendLine(".section{margin:28px 0;padding:16px;background:#fafafa;border:1px solid #e0e0e0;border-radius:8px;}");
        html.AppendLine("img{max-width:100%;height:auto;border:1px solid #ddd;}");
        html.AppendLine(".warn{background:#fff3cd;padding:8px;border-radius:4px;}");
        html.AppendLine(".err{background:#f8d7da;padding:8px;border-radius:4px;}");
        html.AppendLine(".ok{background:#d4edda;padding:8px;border-radius:4px;}");
        html.AppendLine(".toc{margin:20px 0;padding:14px 16px;background:#f8f9ff;border:1px solid #d9def0;border-radius:8px;}");
        html.AppendLine(".toc h2{margin:0 0 10px 0;font-size:18px;}");
        html.AppendLine(".toc ul{margin:0;padding-left:20px;columns:2;column-gap:28px;}");
        html.AppendLine(".toc li{margin:4px 0;break-inside:avoid;}");
        html.AppendLine(".toc a{text-decoration:none;color:#1f4a9d;}");
        html.AppendLine(".toc a:hover{text-decoration:underline;}");
        html.AppendLine(".back-to-toc{position:fixed;right:24px;bottom:24px;z-index:999;display:inline-flex;align-items:center;justify-content:center;padding:0 14px;height:40px;border-radius:999px;background:#1f4a9d;color:#fff;text-decoration:none;font-size:14px;font-weight:700;box-shadow:0 6px 16px rgba(0,0,0,.22);}");
        html.AppendLine(".back-to-toc:hover{background:#173a7b;}");
        html.AppendLine("</style></head><body><div class=\"container\">");
        html.AppendLine("<h1>PixelCompare レポート</h1>");
        html.AppendLine($"<p><strong>作成日時:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine($"<p><strong>Excel:</strong> {excelPath}</p>");
        html.AppendLine($"<p><strong>シート:</strong> {sheetName}</p>");
        html.AppendLine("<div class=\"toc\" id=\"toc\"><h2>目次</h2><ul>");
        foreach (var entry in results.OrderBy(x => x.RowIndex))
        {
            var rowIndex = entry.RowIndex;
            var result = entry.Result;
            var diffCountText = result.IsSizeMismatch ? "∞" : result.HasError ? "-" : result.DiffCount.ToString();
            html.AppendLine($"<li><a href=\"#row-{rowIndex}\">{rowIndex} 行目（差異数: {diffCountText}）</a></li>");
        }
        html.AppendLine("</ul></div>");

        foreach (var entry in results.OrderBy(x => x.RowIndex))
        {
            var rowIndex = entry.RowIndex;
            var result = entry.Result;
            html.AppendLine($"<div class=\"section\" id=\"row-{rowIndex}\"><h3>{rowIndex} 行目</h3>");

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
                html.AppendLine($"<div class=\"ok\">差異数: {result.DiffCount} / 差異率: {result.DifferencePercentage:F2}%</div>");
                AppendImageIfExists(html, result.MarkedImage1Path, "左画像（赤枠）");
                AppendImageIfExists(html, result.MarkedImage2Path, "右画像（赤枠）");
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</div><a class=\"back-to-toc\" href=\"#toc\" title=\"目次へ戻る\" aria-label=\"目次へ戻る\">目次へ戻る</a></body></html>");
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

    private static string? TryLoadFontBase64(string fileName)
    {
        foreach (var path in GetFontCandidatePaths(fileName))
        {
            try
            {
                if (File.Exists(path))
                {
                    return Convert.ToBase64String(File.ReadAllBytes(path));
                }
            }
            catch
            {
                // フォント読込失敗時は通常フォールバックで継続する。
            }
        }

        return null;
    }

    private static IEnumerable<string> GetFontCandidatePaths(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Assets", "Fonts", fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Fonts", fileName);

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 5 && dir is not null; i++)
        {
            yield return Path.Combine(dir.FullName, "Assets", "Fonts", fileName);
            dir = dir.Parent;
        }
    }
}
