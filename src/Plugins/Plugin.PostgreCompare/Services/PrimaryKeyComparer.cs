using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// 主キー辞書の等価比較子（参照: WslPostgreTool CompareService.PrimaryKeyComparer）
/// </summary>
public sealed class PrimaryKeyComparer : IEqualityComparer<Dictionary<string, object?>>
{
    public bool Equals(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
    {
        if (x == null || y == null) return x == y;
        if (x.Count != y.Count) return false;

        foreach (var kvp in x)
        {
            if (!y.TryGetValue(kvp.Key, out var value) || !ValueEquals(kvp.Value, value))
                return false;
        }

        return true;
    }

    public int GetHashCode(Dictionary<string, object?> obj)
    {
        var hash = new HashCode();
        foreach (var kvp in obj.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }

    private static bool ValueEquals(object? x, object? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        return x.Equals(y);
    }
}
