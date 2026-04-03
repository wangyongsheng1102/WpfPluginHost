using System.IO;
using System.Windows;
using Plugin.Abstractions;

namespace ShellApp.ViewModels;

public sealed class PluginMenuItemViewModel
{
    public PluginMenuItemViewModel(IPluginModule module, string sourcePath)
    {
        Module = module;
        SourcePath = sourcePath;
        IconImagePath = ResolveIconImagePath(module.Id, module.IconKey);
    }

    public IPluginModule Module { get; }
    public string SourcePath { get; }
    public string Id => Module.Id;
    public string Title => Module.Title;
    public string Description => string.IsNullOrWhiteSpace(Module.Description) ? Title : Module.Description;
    public string IconGlyph =>
        LooksLikeImagePath(Module.IconKey) || string.IsNullOrWhiteSpace(Module.IconKey)
            ? "◻"
            : Module.IconKey;
    public string? IconImagePath { get; }
    public bool HasIconImage => !string.IsNullOrWhiteSpace(IconImagePath);
    public int Order => Module.Order;

    private static string? ResolveIconImagePath(string moduleId, string iconKey)
    {
        var explicitPath = ResolveFromPath(iconKey);
        if (explicitPath is not null)
        {
            return explicitPath;
        }

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        var normalizedId = moduleId.Trim().ToLowerInvariant();
        foreach (var relative in new[]
                 {
                     Path.Combine("Assets", "Images", $"img_{normalizedId}.png"),
                     Path.Combine("Assets", "Images", $"img_plugin.{normalizedId}.png"),
                 })
        {
            var resolved = ResolveFromPath(relative);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveFromPath(string? pathValue)
    {
        if (!LooksLikeImagePath(pathValue))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(pathValue!)
            ? pathValue!
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, pathValue!));

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        var normalized = pathValue!.Replace('\\', '/').TrimStart('/');
        const string assetPrefix = "Assets/Images/";
        if (!normalized.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // バインド時は「/Assets/...」では埋め込みリソースを解決できない。pack URI のみ返す。
        // 実在確認：無い URI を返すと Image が空のままになり IconGlyph も隠れるため、GetResourceStream で確認する。
        return TryPackUriIfEmbeddedResourceExists(normalized);
    }

    private static string? TryPackUriIfEmbeddedResourceExists(string normalizedAssetsPath)
    {
        if (Application.Current is null)
        {
            return null;
        }

        var asmName = typeof(PluginMenuItemViewModel).Assembly.GetName().Name;
        if (string.IsNullOrEmpty(asmName))
        {
            return null;
        }

        var attempts = new[]
        {
            new Uri($"pack://application:,,,/{normalizedAssetsPath}", UriKind.Absolute),
            new Uri($"pack://application:,,,/{asmName};component/{normalizedAssetsPath}", UriKind.Absolute),
        };

        foreach (var uri in attempts)
        {
            try
            {
                if (Application.GetResourceStream(uri) is not null)
                {
                    return uri.OriginalString;
                }
            }
            catch (UriFormatException)
            {
            }
        }

        return null;
    }

    private static bool LooksLikeImagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
