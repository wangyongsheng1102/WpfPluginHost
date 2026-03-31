namespace Plugin.PixelCompare.Models;

public sealed class CompareOptions
{
    public int DiffThreshold { get; init; } = 30;
    public int MinRegionArea { get; init; } = 50;
    public int MergeDistance { get; init; } = 10;
    public int ExpandPixels { get; init; } = 3;
}
