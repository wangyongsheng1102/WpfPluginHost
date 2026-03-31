using SixLabors.ImageSharp;

namespace Plugin.PixelCompare.Services;

public sealed class DifferenceRegionService
{
    public bool[,] ExpandDiffMap(bool[,] diffMap, int width, int height, int expandPixels)
    {
        var expanded = new bool[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!diffMap[x, y])
                {
                    continue;
                }

                for (var dy = -expandPixels; dy <= expandPixels; dy++)
                {
                    for (var dx = -expandPixels; dx <= expandPixels; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            expanded[nx, ny] = true;
                        }
                    }
                }
            }
        }

        return expanded;
    }

    public List<Rectangle> FindDifferenceRegions(bool[,] diffMap, int width, int height, int minArea)
    {
        var regions = new List<Rectangle>();
        var visited = new bool[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!diffMap[x, y] || visited[x, y])
                {
                    continue;
                }

                var region = FindConnectedRegion(diffMap, visited, width, height, x, y);
                if (region.Width * region.Height >= minArea)
                {
                    regions.Add(region);
                }
            }
        }

        return regions;
    }

    public List<Rectangle> MergeNearbyRegions(List<Rectangle> regions, int mergeDistance)
    {
        if (regions.Count <= 1)
        {
            return regions;
        }

        var merged = new List<Rectangle>();
        var used = new bool[regions.Count];

        for (var i = 0; i < regions.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            var current = regions[i];
            used[i] = true;
            var foundNearby = true;
            while (foundNearby)
            {
                foundNearby = false;
                for (var j = i + 1; j < regions.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    var other = regions[j];
                    var distance = CalculateRectDistance(current, other);
                    if (distance > mergeDistance)
                    {
                        continue;
                    }

                    var minX = Math.Min(current.X, other.X);
                    var minY = Math.Min(current.Y, other.Y);
                    var maxX = Math.Max(current.X + current.Width, other.X + other.Width);
                    var maxY = Math.Max(current.Y + current.Height, other.Y + other.Height);
                    current = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                    used[j] = true;
                    foundNearby = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private static Rectangle FindConnectedRegion(bool[,] diffMap, bool[,] visited, int width, int height, int startX, int startY)
    {
        var minX = startX;
        var maxX = startX;
        var minY = startY;
        var maxY = startY;

        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        visited[startX, startY] = true;

        var directions = new[] { (0, 1), (0, -1), (1, 0), (-1, 0) };
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);

            foreach (var (dx, dy) in directions)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height || !diffMap[nx, ny] || visited[nx, ny])
                {
                    continue;
                }

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int CalculateRectDistance(Rectangle r1, Rectangle r2)
    {
        var r1Right = r1.X + r1.Width;
        var r1Bottom = r1.Y + r1.Height;
        var r2Right = r2.X + r2.Width;
        var r2Bottom = r2.Y + r2.Height;

        if (r1.X <= r2Right && r2.X <= r1Right && r1.Y <= r2Bottom && r2.Y <= r1Bottom)
        {
            return 0;
        }

        var dx = Math.Max(0, Math.Max(r1.X - r2Right, r2.X - r1Right));
        var dy = Math.Max(0, Math.Max(r1.Y - r2Bottom, r2.Y - r1Bottom));
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }
}
