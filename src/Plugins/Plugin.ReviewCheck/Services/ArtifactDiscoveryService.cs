using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Plugin.ReviewCheck.Services;

public sealed class ArtifactDiscoveryService
{
    public DocumentDiscoveryResult Discover(string svnRootPath, string? functionId)
    {
        if (string.IsNullOrWhiteSpace(svnRootPath) || !Directory.Exists(svnRootPath))
        {
            return new DocumentDiscoveryResult(null, null, null, null);
        }

        var candidates = Directory.EnumerateFileSystemEntries(svnRootPath, "*", SearchOption.AllDirectories)
            .Where(p => !IsHiddenPath(p))
            .ToList();

        IEnumerable<string> scoped = candidates;
        if (!string.IsNullOrWhiteSpace(functionId))
        {
            scoped = scoped.Where(p => p.Contains(functionId, StringComparison.OrdinalIgnoreCase));
        }

        var specExcel = FirstMatch(scoped.Where(IsExcelFile),
            "単体テスト仕様書", "単体テスト", "テスト仕様書", "仕様書", "UT");

        var artifactsList = FirstMatch(scoped.Where(IsExcelFile),
            "成果物一覧", "成果物リスト", "成果物");

        var manualFix = FirstMatch(scoped.Where(IsExcelFile),
            "手修正確認結果", "手修正", "修正確認結果");

        var evidenceFolder = FirstMatch(scoped.Where(Directory.Exists),
            "エビデンス", "evidence");

        return new DocumentDiscoveryResult(
            SpecExcelPath: specExcel,
            EvidenceFolderPath: evidenceFolder,
            ArtifactsListPath: artifactsList,
            ManualFixResultPath: manualFix);
    }

    public IReadOnlyList<CheckResultItem> ToCheckItems(DocumentDiscoveryResult discovery)
    {
        var items = new List<CheckResultItem>();

        items.Add(ToExistenceItem("document", "仕様書Excel", discovery.SpecExcelPath));
        items.Add(ToExistenceItem("document", "成果物一覧", discovery.ArtifactsListPath));
        items.Add(ToExistenceItem("document", "手修正確認結果", discovery.ManualFixResultPath));
        items.Add(ToExistenceItem("document", "エビデンスフォルダ", discovery.EvidenceFolderPath, isFolder: true));

        return items;
    }

    private static CheckResultItem ToExistenceItem(string step, string label, string? path, bool isFolder = false)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && (isFolder ? Directory.Exists(path) : File.Exists(path));
        return new CheckResultItem(
            Step: step,
            Status: exists ? "〇" : "✕",
            Severity: exists ? CheckSeverity.Info : CheckSeverity.Error,
            SheetName: "",
            CellRef: "",
            Message: exists ? $"{label}: {path}" : $"{label} が見つかりません。検索条件を調整してください。");
    }

    private static string? FirstMatch(IEnumerable<string> paths, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            var hit = paths.FirstOrDefault(p => p.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit))
            {
                return hit;
            }
        }

        return paths.FirstOrDefault();
    }

    private static bool IsExcelFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".xls", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHiddenPath(string path)
    {
        // 「.git」や「.svn」などを雑に除外（誤爆を避けるため最低限）
        return path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.svn{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
