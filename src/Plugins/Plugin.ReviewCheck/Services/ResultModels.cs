namespace Plugin.ReviewCheck.Services;

public enum CheckSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record CheckResultItem(
    string Step,
    string Status,
    CheckSeverity Severity,
    string SheetName,
    string CellRef,
    string Message);

public sealed class ReviewCheckRequest
{
    public required string SvnRootPath { get; init; }
    public string? WbsExcelPath { get; init; }
    public string? FunctionId { get; init; }
}

public sealed record DocumentDiscoveryResult(
    string? SpecExcelPath,
    string? EvidenceFolderPath,
    string? ArtifactsListPath,
    string? ManualFixResultPath);

public sealed record WbsPeople(string? Author, string? Reviewer);

public sealed record ExcelContentCheckResult(string WorkbookPath, IReadOnlyList<CheckResultItem> Items);
