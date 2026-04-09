namespace Plugin.PixelCompare.Models;

public sealed class CompareOptions
{
    public int DiffThreshold { get; set; } = 30;
    public int MinRegionArea { get; set; } = 50;
    public int MergeDistance { get; set; } = 10;
    public int ExpandPixels { get; set; } = 3;
}
