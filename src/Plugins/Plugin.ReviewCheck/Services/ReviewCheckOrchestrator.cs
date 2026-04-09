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
        var discovery = _discoveryService.Discover(request.SvnRootPath, request.FunctionId);

        results.AddRange(_discoveryService.ToCheckItems(discovery));

        cancellationToken.ThrowIfCancellationRequested();

        _context?.ReportProgress("WBS を解析中…", 20, true);
        WbsPeople people = new(null, null);
        if (!string.IsNullOrWhiteSpace(request.WbsExcelPath))
        {
            try
            {
                people = await Task.Run(() => _wbsLookupService.TryReadPeople(request.WbsExcelPath!), cancellationToken)
                    .ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(discovery.SpecExcelPath))
        {
            _context?.ReportProgress("Excel 記入内容をチェック中…", 50, true);
            var excelResult = await Task.Run(() => _excelRules.Check(discovery.SpecExcelPath!, people), cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(excelResult.Items);
        }
        else
        {
            results.Add(new CheckResultItem(
                Step: "content",
                Status: "ー",
                Severity: CheckSeverity.Info,
                SheetName: "",
                CellRef: "",
                Message: "仕様書 Excel が見つからないため、記入内容チェックをスキップしました。"));
        }

        _context?.ReportProgress("完了", 100, false);
        return results;
    }
}
