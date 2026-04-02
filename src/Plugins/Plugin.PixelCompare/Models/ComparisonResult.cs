namespace Plugin.PixelCompare.Models;

public sealed class ComparisonResult
{
    public double DifferencePercentage { get; init; }
    public int DiffCount { get; init; }
    public bool IsSizeMismatch { get; init; }
    public string SizeInfo { get; init; } = string.Empty;
    public bool HasError { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string? DifferenceImagePath { get; init; }
    public string? MarkedImage1Path { get; init; }
    public string? MarkedImage2Path { get; init; }
    public string? Image1Path { get; init; }
    public string? Image2Path { get; init; }
}
