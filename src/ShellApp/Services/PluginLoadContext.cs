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
        }

        return null;
    }
}
