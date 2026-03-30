namespace ShellApp.Services;

/// <summary>
/// シェルの既定 <see cref="System.Runtime.Loader.AssemblyLoadContext"/> によって読み込まれるアセンブリ。
/// プラグイン用 ALC はこれらの名前では null を返し、メインの出力ディレクトリから解決する。plugins フォルダに同梱不要。
/// 共有依存を追加する場合：ShellApp.csproj に同バージョンの PackageReference を追加し、単純名をこの集合に加える。
/// </summary>
internal static class PluginHostSharedAssemblies
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plugin.Abstractions",
        "CommunityToolkit.Mvvm",
    };

    public static bool IsHostProvided(string? assemblySimpleName) =>
        !string.IsNullOrEmpty(assemblySimpleName) && Names.Contains(assemblySimpleName);
}
