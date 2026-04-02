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

        // 統一規約によるフォールバック: Assets/Images/img_{id}.png
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

        // 1) 如果仍然存在于文件系统（例如开发环境或你显式保留拷贝），优先返回绝对路径
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // 2) 否则尝试从 WPF 资源加载：
        //    当 Assets/Images 作为 Resource 嵌入到程序集后，Image 可以直接用 pack URI 加载。
        var normalized = pathValue!.Replace("\\", "/").TrimStart('/');
        const string assetPrefix = "Assets/Images/";
        if (normalized.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "/" + normalized;
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
