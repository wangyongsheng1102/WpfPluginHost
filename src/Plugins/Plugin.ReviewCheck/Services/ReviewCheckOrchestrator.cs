using Plugin.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.ReviewCheck.Services;

public sealed class ReviewCheckOrchestrator
{
    private readonly IPluginContext? _context;
    private readonly ArtifactDiscoveryService _discoveryService = new();
    private readonly WbsLookupService _wbsLookupService = new();
    private readonly ExcelContentRuleService _excelRules = new();

    public ReviewCheckOrchestrator(IPluginContext? context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CheckResultItem>> RunAsync(ReviewCheckRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _context?.ReportProgress("文書の探索中…", 0, true);

        var results = new List<CheckResultItem>();
        var discovery = _discoveryService.Discover(request);

        results.AddRange(_discoveryService.ToCheckItems(discovery));

        cancellationToken.ThrowIfCancellationRequested();

        _context?.ReportProgress("WBS を解析中…", 20, true);
        WbsPeople? people = null;
        if (!string.IsNullOrWhiteSpace(request.WbsExcelPath))
        {
            try
            {
                people = await Task.Run(() =>
                        _wbsLookupService.TryReadPeople(
                            request.WbsExcelPath!,
                            request.FunctionId,
                            request.SystemName,
                            request.ObjectName),
                    cancellationToken)
                    .ConfigureAwait(false);

                if (people is null)
                {
                    results.Add(new CheckResultItem(
                        Step: "content",
                        Status: "✕",
                        Severity: CheckSeverity.Warning,
                        SheetName: "WBS",
                        CellRef: "",
                        Message: "WBS から機能ID・System・对象に一致する行を見つけられませんでした。"));
                }
            }
            catch (Exception ex)
            {
                results.Add(new CheckResultItem(
                    Step: "WBS",
                    Status: "✕",
                    Severity: CheckSeverity.Warning,
                    SheetName: "",
                    CellRef: "",
                    Message: $"WBS 解析に失敗: {ex.Message}"));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        _context?.ReportProgress("Excel 記入内容をチェック中…", 50, true);

        var anyWorkbookChecked = false;
        foreach (var artifact in discovery.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Path))
            {
                continue;
            }
            if (artifact.IsFolder)
            {
                continue;
            }

            var key = artifact.Key;
            var label = artifact.Label;
            var path = artifact.Path!;

            var excelResult = await Task.Run(() => _excelRules.Check(path, key, label, people), cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(excelResult.Items);
            anyWorkbookChecked = true;
        }

        if (!anyWorkbookChecked)
        {
            results.Add(new CheckResultItem(
                Step: "content",
                Status: "ー",
                Severity: CheckSeverity.Warning,
                SheetName: "",
                CellRef: "",
                Message: "content チェック対象の Excel が見つからないためスキップしました。"));
        }

        _context?.ReportProgress("完了", 100, false);
        return results;
    }
}
