using SixLabors.ImageSharp;

namespace Plugin.PixelCompare.Services;

public sealed class DifferenceRegionService
{
    /// <summary>
    /// Separable box expansion using two sliding-window passes (horizontal + vertical).
    /// Reduces complexity from O(N * expand²) to O(N) amortized, with each pass parallelized across rows/columns.
    /// </summary>
    public byte[] ExpandDiffMap(byte[] diffMap, int width, int height, int expandPixels)
    {
        if (expandPixels <= 0)
        {
            var copy = new byte[diffMap.Length];
            Buffer.BlockCopy(diffMap, 0, copy, 0, diffMap.Length);
            return copy;
        }

        var temp = new byte[width * height];
        Parallel.For(0, height, y =>
        {
            var rowOffset = y * width;
            var count = 0;

            for (var i = 0; i < Math.Min(expandPixels, width); i++)
            {
                count += diffMap[rowOffset + i];
            }

            for (var x = 0; x < width; x++)
            {
                var right = x + expandPixels;
                if (right < width)
                {
                    count += diffMap[rowOffset + right];
                }

                if (count > 0)
                {
                    temp[rowOffset + x] = 1;
                }

                var left = x - expandPixels;
                if (left >= 0)
                {
                    count -= diffMap[rowOffset + left];
                }
            }
        });

        var expanded = new byte[width * height];
        Parallel.For(0, width, x =>
        {
            var count = 0;

            for (var i = 0; i < Math.Min(expandPixels, height); i++)
            {
                count += temp[i * width + x];
            }

            for (var y = 0; y < height; y++)
            {
                var bottom = y + expandPixels;
                if (bottom < height)
                {
                    count += temp[bottom * width + x];
                }

                if (count > 0)
                {
                    expanded[y * width + x] = 1;
                }

                var top = y - expandPixels;
                if (top >= 0)
                {
                    count -= temp[top * width + x];
                }
            }
        });

        return expanded;
    }

    public List<Rectangle> FindDifferenceRegions(byte[] diffMap, int width, int height, int minArea)
    {
        var regions = new List<Rectangle>();
        var visited = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x;
                if (diffMap[idx] == 0 || visited[idx] != 0)
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
        var mergeDistanceSq = mergeDistance * mergeDistance;

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
                    if (CalculateRectDistanceSquared(current, other) > mergeDistanceSq)
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

    /// <summary>
    /// BFS with 1D linear indices — avoids tuple allocation overhead
    /// and improves cache locality via row-major byte[] arrays.
    /// </summary>
    private static Rectangle FindConnectedRegion(byte[] diffMap, byte[] visited, int width, int height, int startX, int startY)
    {
        var minX = startX;
        var maxX = startX;
        var minY = startY;
        var maxY = startY;
        var totalSize = width * height;

        var queue = new Queue<int>();
        var startIdx = startY * width + startX;
        queue.Enqueue(startIdx);
        visited[startIdx] = 1;

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var y = idx / width;
            var x = idx - y * width;

            if (x < minX) minX = x;
            else if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            else if (y > maxY) maxY = y;

            if (x + 1 < width)
            {
                var ni = idx + 1;
                if (diffMap[ni] != 0 && visited[ni] == 0)
                {
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }

            if (x > 0)
            {
                var ni = idx - 1;
                if (diffMap[ni] != 0 && visited[ni] == 0)
                {
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }

            if (idx + width < totalSize)
            {
                var ni = idx + width;
                if (diffMap[ni] != 0 && visited[ni] == 0)
                {
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }

            if (idx >= width)
            {
                var ni = idx - width;
                if (diffMap[ni] != 0 && visited[ni] == 0)
                {
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }
        }

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int CalculateRectDistanceSquared(Rectangle r1, Rectangle r2)
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
        return dx * dx + dy * dy;
    }
}
