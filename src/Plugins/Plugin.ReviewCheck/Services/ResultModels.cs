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
    public string? SystemName { get; init; }
    public string? ObjectName { get; init; }
    public string? InputType { get; init; }
}

public sealed class DocumentDiscoveryResult
{
    public string? TargetFolderPath { get; init; }
    public IReadOnlyList<DocumentArtifact> Artifacts { get; init; } = Array.Empty<DocumentArtifact>();

    public string? GetPath(string key)
    {
        return Artifacts.FirstOrDefault(x => x.Key == key)?.Path;
    }
}

public sealed class DocumentArtifact
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Keyword { get; init; }
    public bool Required { get; init; }
    public bool IsFolder { get; init; }
    public string? Path { get; set; }
}

public sealed class WbsPeople
{
    public string? CodeUserName { get; init; }
    public string? CodeUserDate { get; init; }
    public string? CodeReviewUserName { get; init; }
    public string? CodeReviewUserDate { get; init; }
    public string? TestUserName { get; init; }
    public string? TestUserDate { get; init; }
    public string? TestReviewUserName { get; init; }
    public string? TestReviewUserDate { get; init; }
}

public sealed record ExcelContentCheckResult(string WorkbookPath, IReadOnlyList<CheckResultItem> Items);
