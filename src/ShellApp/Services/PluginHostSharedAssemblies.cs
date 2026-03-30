namespace ShellApp.Services;

/// <summary>
/// 由 Shell 默认 <see cref="System.Runtime.Loader.AssemblyLoadContext"/> 加载的程序集。
/// 插件 ALC 遇到这些名称时返回 null，从主程序输出目录解析，plugins 文件夹不必再带这些 DLL。
/// 新增共享依赖时：在 ShellApp.csproj 添加同版本 PackageReference，并把程序集简单名称加入集合。
/// </summary>
internal static class PluginHostSharedAssemblies
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plugin.Abstractions",
        "CommunityToolkit.Mvvm",
        "Microsoft.Office.Interop.Excel",
    };

    public static bool IsHostProvided(string? assemblySimpleName) =>
        !string.IsNullOrEmpty(assemblySimpleName) && Names.Contains(assemblySimpleName);
}
