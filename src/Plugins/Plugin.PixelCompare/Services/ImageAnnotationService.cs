using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plugin.PixelCompare.Services;

public sealed class ImageAnnotationService
{
    public void DrawRectanglesWithIndexOutside(Image<Rgba32> image, List<Rectangle> rectangles)
    {
        var rectColor = Color.Red;
        var textColor = Color.DarkSlateGray;
        const int lineWidth = 2;
        const float fontSize = 30f;
        var font = SystemFonts.CreateFont("Arial", fontSize, FontStyle.Bold);

        image.Mutate(ctx =>
        {
            for (var i = 0; i < rectangles.Count; i++)
            {
                var rect = rectangles[i];
                var x = Math.Max(0, rect.X);
                var y = Math.Max(0, rect.Y);
                var w = Math.Min(rect.Width, image.Width - x);
                var h = Math.Min(rect.Height, image.Height - y);
                var safeRect = new Rectangle(x, y, w, h);
                ctx.Draw(rectColor, lineWidth, safeRect);

                var label = (i + 1).ToString();
                var textPos = CalcOutsideTextPosition(safeRect, label, fontSize, image.Width, image.Height);
                ctx.DrawText(label, font, textColor, textPos);
            }
        });
    }

    private static PointF CalcOutsideTextPosition(Rectangle rect, string text, float fontSize, int imageWidth, int imageHeight)
    {
        const float padding = 4f;
        var approxCharWidth = fontSize * 0.6f;
        var textWidth = approxCharWidth * text.Length;
        var textHeight = fontSize;

        var x = rect.Left - textWidth - padding;
        var y = (float)rect.Top;

        if (x < 0)
        {
            x = rect.Right + padding;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (y + textHeight > imageHeight)
        {
            y = imageHeight - textHeight;
        }

        if (x + textWidth > imageWidth)
        {
            x = Math.Max(0, rect.Left - textWidth - padding);
        }

        return new PointF(x, y);
    }
}
