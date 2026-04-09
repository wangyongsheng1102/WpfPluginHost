using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Plugin.ReviewCheck.Services;

public sealed class ArtifactDiscoveryService
{
    public DocumentDiscoveryResult Discover(ReviewCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SvnRootPath) || !Directory.Exists(request.SvnRootPath))
        {
            return new DocumentDiscoveryResult();
        }

        var namingToken = string.IsNullOrWhiteSpace(request.FunctionId)
            ? string.Empty
            : $"({request.FunctionId}_";
        var systemToken = ResolveSystemToken(request.SystemName, request.InputType);
        var objectToken = request.ObjectName ?? string.Empty;

        var files = Directory.EnumerateFiles(request.SvnRootPath, "*", SearchOption.AllDirectories)
            .Where(p => !IsHiddenPath(p))
            .Where(IsExcelFile)
            .Where(p => !string.IsNullOrWhiteSpace(namingToken) ? Path.GetFileName(p).Contains(namingToken, StringComparison.OrdinalIgnoreCase) : true)
            .Where(p => IsSystemMatched(Path.GetDirectoryName(p) ?? string.Empty, systemToken))
            .Where(p => IsObjectMatched(p, objectToken))
            .ToList();

        var specKeyword = IsIntegrationObject(request.ObjectName) ? "結合テスト仕様書" : "単体テスト仕様書";
        var evidenceKeyword = IsIntegrationObject(request.ObjectName) ? "結合テストエビデンス" : "単体テストエビデンス";

        var artifacts = new List<DocumentArtifact>
        {
            new() { Key = "EXCEL_TEST", Label = "テスト仕様書", Keyword = specKeyword, Required = true, IsFolder = false },
            new() { Key = "EXCEL_EVIDENCE", Label = "エビデンス", Keyword = evidenceKeyword, Required = true, IsFolder = false },
            new() { Key = "EXCEL_LIST", Label = "成果物一覧", Keyword = "成果物一覧", Required = true, IsFolder = false },
            new() { Key = "EXCEL_COMPARE", Label = "手修正確認結果", Keyword = "手修正確認結果", Required = false, IsFolder = false },
            new() { Key = "EXCEL_COVERAGE", Label = "カバレッジ結果", Keyword = "カバレッジ結果", Required = false, IsFolder = false },
            new() { Key = "EXCEL_REVIEW", Label = "レビュー記録表", Keyword = "レビュー記録", Required = false, IsFolder = false },
            new() { Key = "EXCEL_CD_CHECKLIST", Label = "CDチェックリスト", Keyword = "CDチェックリスト", Required = false, IsFolder = false },
            new() { Key = "EXCEL_UT_CHECKLIST", Label = "UTチェックリスト", Keyword = "UTチェックリスト", Required = false, IsFolder = false }
        };

        foreach (var artifact in artifacts)
        {
            artifact.Path = FindByKeyword(files, artifact.Keyword);
            if (artifact.Path is null && artifact.Key == "EXCEL_COMPARE")
            {
                artifact.Path = FindByKeyword(files, "手修正");
            }
        }

        var targetFolder = artifacts
            .Select(a => a.Path)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

        return new DocumentDiscoveryResult
        {
            TargetFolderPath = targetFolder is null ? null : Path.GetDirectoryName(targetFolder),
            Artifacts = artifacts
        };
    }

    public IReadOnlyList<CheckResultItem> ToCheckItems(DocumentDiscoveryResult discovery)
    {
        var items = new List<CheckResultItem>();

        items.Add(new CheckResultItem(
            Step: "document",
            Status: string.IsNullOrWhiteSpace(discovery.TargetFolderPath) ? "✕" : "〇",
            Severity: string.IsNullOrWhiteSpace(discovery.TargetFolderPath) ? CheckSeverity.Error : CheckSeverity.Info,
            SheetName: "",
            CellRef: "",
            Message: string.IsNullOrWhiteSpace(discovery.TargetFolderPath)
                ? "機能ID命名規則に一致するフォルダ/ファイルが見つかりません。"
                : $"対象フォルダ: {discovery.TargetFolderPath}"));

        foreach (var artifact in discovery.Artifacts)
        {
            items.Add(ToExistenceItem("document", artifact.Label, artifact.Path, artifact.Required, artifact.IsFolder));
        }

        return items;
    }

    private static CheckResultItem ToExistenceItem(string step, string label, string? path, bool required, bool isFolder = false)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && (isFolder ? Directory.Exists(path) : File.Exists(path));
        return new CheckResultItem(
            Step: step,
            Status: exists ? "〇" : (required ? "✕" : "ー"),
            Severity: exists ? CheckSeverity.Info : (required ? CheckSeverity.Error : CheckSeverity.Warning),
            SheetName: "",
            CellRef: "",
            Message: exists
                ? $"{label}: {path}"
                : required
                    ? $"{label} が見つかりません。検索条件を調整してください。"
                    : $"{label} は見つかりませんでした（任意項目）。");
    }

    private static string? FindByKeyword(IEnumerable<string> paths, string keyword)
    {
        return paths
            .Where(p => Path.GetFileName(p).Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Length)
            .FirstOrDefault();
    }

    private static bool IsSystemMatched(string rootPath, string? systemToken)
    {
        if (string.IsNullOrWhiteSpace(systemToken))
        {
            return true;
        }

        var normalized = rootPath.Replace("/", "\\");
        return normalized.Contains("\\" + systemToken + "\\", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("\\" + systemToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObjectMatched(string path, string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return true;
        }

        return path.Contains(objectName, StringComparison.OrdinalIgnoreCase)
               || objectName.Contains("結合", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSystemToken(string? systemName, string? inputType)
    {
        if (!string.IsNullOrWhiteSpace(inputType) && inputType.Equals("API", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return systemName switch
        {
            "EnabilityCis" => "cis",
            "EnabilityOrder" => "order",
            "EnabilityPortal" => "portal",
            "EnabilityPortal2" => "portal2",
            _ => string.IsNullOrWhiteSpace(systemName) ? null : systemName
        };
    }

    private static bool IsIntegrationObject(string? objectName)
    {
        return !string.IsNullOrWhiteSpace(objectName) && objectName.Contains("結合", StringComparison.OrdinalIgnoreCase);
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
