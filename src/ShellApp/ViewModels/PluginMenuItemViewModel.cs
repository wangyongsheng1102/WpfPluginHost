using Plugin.Abstractions;
using System.IO;

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

        // Unified convention fallback: Assets/Images/img_{id}.png
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        var normalizedId = moduleId.Trim().ToLowerInvariant();
        var conventionRelativePath = Path.Combine("Assets", "Images", $"img_{normalizedId}.png");
        return ResolveFromPath(conventionRelativePath);
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

        return File.Exists(fullPath) ? fullPath : null;
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
