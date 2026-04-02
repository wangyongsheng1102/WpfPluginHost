using Plugin.PixelCompare.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace Plugin.PixelCompare.Services;

public sealed class ImageComparisonService
{
    private readonly DifferenceRegionService _differenceRegionService;
    private readonly ImageAnnotationService _imageAnnotationService;

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
                var differentPixels = 0;
                var diffMap = new bool[width, height];
                var threshold = options.DiffThreshold;

                img1.ProcessPixelRows(img2, (accessor1, accessor2) =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        var row1 = accessor1.GetRowSpan(y);
                        var row2 = accessor2.GetRowSpan(y);
                        for (var x = 0; x < width; x++)
                        {
                            ref var p1 = ref row1[x];
                            ref var p2 = ref row2[x];

                            var diffR = Math.Abs(p1.R - p2.R);
                            var diffG = Math.Abs(p1.G - p2.G);
                            var diffB = Math.Abs(p1.B - p2.B);

                            if (diffR > threshold || diffG > threshold || diffB > threshold)
                            {
                                differentPixels++;
                                diffMap[x, y] = true;
                            }
                        }
                    }
                });

                var expanded = _differenceRegionService.ExpandDiffMap(diffMap, width, height, options.ExpandPixels);
                var regions = _differenceRegionService.FindDifferenceRegions(expanded, width, height, options.MinRegionArea);
                regions = _differenceRegionService.MergeNearbyRegions(regions, options.MergeDistance);

                using var diffImage = new Image<Rgba32>(width, height);
                diffImage.ProcessPixelRows(img1, img2, (accessorDiff, accessor1, accessor2) =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        var rowDiff = accessorDiff.GetRowSpan(y);
                        var row1 = accessor1.GetRowSpan(y);
                        var row2 = accessor2.GetRowSpan(y);
                        for (var x = 0; x < width; x++)
                        {
                            if (!diffMap[x, y])
                            {
                                rowDiff[x] = new Rgba32(0, 0, 0, 255); // 背景は黒
                                continue;
                            }

                            var p1 = row1[x];
                            var p2 = row2[x];
                            var r = (byte)Math.Min(255, Math.Abs(p1.R - p2.R) * 3);
                            var g = (byte)Math.Min(255, Math.Abs(p1.G - p2.G) * 3);
                            var b = (byte)Math.Min(255, Math.Abs(p1.B - p2.B) * 3);
                            rowDiff[x] = new Rgba32(r, g, b, 255);
                        }
                    }
                });

                using var markedImage1 = img1.Clone();
                using var markedImage2 = img2.Clone();
                _imageAnnotationService.DrawRectanglesWithIndexOutside(markedImage1, regions);
                _imageAnnotationService.DrawRectanglesWithIndexOutside(markedImage2, regions);

                var tempDir = Path.Combine(Path.GetTempPath(), "PixelCompareSuite");
                Directory.CreateDirectory(tempDir);
                var guid = Guid.NewGuid().ToString("N");

                var diffImagePath = Path.Combine(tempDir, $"diff_{rowIndex}_{guid}.png");
                var markedImage1Path = Path.Combine(tempDir, $"marked1_{rowIndex}_{guid}.png");
                var markedImage2Path = Path.Combine(tempDir, $"marked2_{rowIndex}_{guid}.png");

                diffImage.Save(diffImagePath, new PngEncoder());
                markedImage1.Save(markedImage1Path, new PngEncoder());
                markedImage2.Save(markedImage2Path, new PngEncoder());

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
