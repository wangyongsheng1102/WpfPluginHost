namespace Plugin.PixelCompare.Models;

public sealed class ExtractedImagePair
{
    public required string SheetName { get; init; }
    public required int RowIndex { get; init; }
    public required string Image1Path { get; init; }
    public required string Image2Path { get; init; }
}
