using Plugin.PixelCompare.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace Plugin.PixelCompare.Services;

public sealed class ImageComparisonService
{
    private readonly DifferenceRegionService _differenceRegionService;
    private readonly ImageAnnotationService _imageAnnotationService;
    private static readonly PngEncoder FastPngEncoder = new() { CompressionLevel = PngCompressionLevel.BestSpeed };

    public ImageComparisonService(
        DifferenceRegionService differenceRegionService,
        ImageAnnotationService imageAnnotationService)
    {
        _differenceRegionService = differenceRegionService;
        _imageAnnotationService = imageAnnotationService;
    }

    public Task<ComparisonResult> CompareAsync(string image1Path, string image2Path, int rowIndex, CompareOptions options)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(image1Path) || !File.Exists(image2Path))
            {
                return new ComparisonResult
                {
                    HasError = true,
                    ErrorMessage = "画像ファイルが存在しません。"
                };
            }

            try
            {
                using var img1 = Image.Load<Rgba32>(image1Path);
                using var img2 = Image.Load<Rgba32>(image2Path);

                var isSizeMatch = img1.Width == img2.Width && img1.Height == img2.Height;
                var sizeInfo = $"画像①: {img1.Width}x{img1.Height}, 画像②: {img2.Width}x{img2.Height}";
                if (!isSizeMatch)
                {
                    return new ComparisonResult
                    {
                        IsSizeMismatch = true,
                        SizeInfo = sizeInfo
                    };
                }

                var width = img1.Width;
                var height = img1.Height;
                var totalPixels = (long)width * height;
                var threshold = options.DiffThreshold;
                var diffMap = new byte[width * height];
                var differentPixels = 0;

                using var diffImage = new Image<Rgba32>(width, height);

                // Single merged pass: detect pixel differences AND generate diff image simultaneously.
                // Eliminates the original second full-image traversal.
                diffImage.ProcessPixelRows(img1, img2, (accessorDiff, accessor1, accessor2) =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        var rowDiff = accessorDiff.GetRowSpan(y);
                        var row1 = accessor1.GetRowSpan(y);
                        var row2 = accessor2.GetRowSpan(y);
                        var offset = y * width;

                        for (var x = 0; x < width; x++)
                        {
                            ref var p1 = ref row1[x];
                            ref var p2 = ref row2[x];
                            var dR = Math.Abs(p1.R - p2.R);
                            var dG = Math.Abs(p1.G - p2.G);
                            var dB = Math.Abs(p1.B - p2.B);

                            if (dR > threshold || dG > threshold || dB > threshold)
                            {
                                differentPixels++;
                                diffMap[offset + x] = 1;
                                rowDiff[x] = new Rgba32(
                                    (byte)Math.Min(255, dR * 3),
                                    (byte)Math.Min(255, dG * 3),
                                    (byte)Math.Min(255, dB * 3),
                                    255);
                            }
                            else
                            {
                                rowDiff[x] = new Rgba32(0, 0, 0, 255);
                            }
                        }
                    }
                });

                var expanded = _differenceRegionService.ExpandDiffMap(diffMap, width, height, options.ExpandPixels);
                var regions = _differenceRegionService.FindDifferenceRegions(expanded, width, height, options.MinRegionArea);
                regions = _differenceRegionService.MergeNearbyRegions(regions, options.MergeDistance);

                // Draw directly on loaded images — avoids two expensive Clone() allocations
                _imageAnnotationService.DrawRectanglesWithIndexOutside(img1, regions);
                _imageAnnotationService.DrawRectanglesWithIndexOutside(img2, regions);

                var tempDir = Path.Combine(Path.GetTempPath(), "PixelCompareSuite");
                Directory.CreateDirectory(tempDir);
                var guid = Guid.NewGuid().ToString("N");

                var diffImagePath = Path.Combine(tempDir, $"diff_{rowIndex}_{guid}.png");
                var markedImage1Path = Path.Combine(tempDir, $"marked1_{rowIndex}_{guid}.png");
                var markedImage2Path = Path.Combine(tempDir, $"marked2_{rowIndex}_{guid}.png");

                Parallel.Invoke(
                    () => diffImage.Save(diffImagePath, FastPngEncoder),
                    () => img1.Save(markedImage1Path, FastPngEncoder),
                    () => img2.Save(markedImage2Path, FastPngEncoder)
                );

                return new ComparisonResult
                {
                    DifferencePercentage = (double)differentPixels / totalPixels * 100,
                    DiffCount = regions.Count,
                    IsSizeMismatch = false,
                    SizeInfo = sizeInfo,
                    DifferenceImagePath = diffImagePath,
                    MarkedImage1Path = markedImage1Path,
                    MarkedImage2Path = markedImage2Path,
                    Image1Path = image1Path,
                    Image2Path = image2Path
                };
            }
            catch (Exception ex)
            {
                return new ComparisonResult
                {
                    HasError = true,
                    ErrorMessage = ex.Message
                };
            }
        });
    }
}
