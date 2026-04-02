using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ShellApp.Services;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string mainAssemblyToLoadPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyToLoadPath);
        _pluginDirectory = Path.GetDirectoryName(mainAssemblyToLoadPath)
            ?? throw new ArgumentException("無効なアセンブリパスです。", nameof(mainAssemblyToLoadPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (PluginHostSharedAssemblies.IsHostProvided(assemblyName.Name))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null && File.Exists(assemblyPath))
            return LoadFromAssemblyPath(assemblyPath);

        // deps.json では lib/net5.0/xxx.dll などになるが、プラグインディレクトリはフラットコピー。Resolver が null のときは同じフォルダのファイル名で読み込む
        if (!string.IsNullOrEmpty(assemblyName.Name))
        {
            var flat = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
            if (File.Exists(flat))
                return LoadFromAssemblyPath(flat);

            // オプション: 依存 DLL を plugins/Plugin.XXX/lib/ に分けた場合
            var lib = Path.Combine(_pluginDirectory, "lib", assemblyName.Name + ".dll");
            if (File.Exists(lib))
                return LoadFromAssemblyPath(lib);

            // サテライト（言語リソース）: ja\PluginName.resources.dll など
            if (!string.IsNullOrEmpty(assemblyName.CultureName))
            {
                var satellite = Path.Combine(_pluginDirectory, assemblyName.CultureName, assemblyName.Name + ".dll");
                if (File.Exists(satellite))
                    return LoadFromAssemblyPath(satellite);
            }
        }

        return null;
    }
}
