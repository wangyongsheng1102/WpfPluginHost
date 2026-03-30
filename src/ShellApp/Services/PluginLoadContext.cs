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
            ?? throw new ArgumentException("无效的程序集路径。", nameof(mainAssemblyToLoadPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (PluginHostSharedAssemblies.IsHostProvided(assemblyName.Name))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null && File.Exists(assemblyPath))
            return LoadFromAssemblyPath(assemblyPath);

        // deps.json 里常为 lib/net5.0/xxx.dll，而插件目录是扁平复制；Resolver 会返回 null，必须在同目录按文件名加载
        if (!string.IsNullOrEmpty(assemblyName.Name))
        {
            var flat = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
            if (File.Exists(flat))
                return LoadFromAssemblyPath(flat);
        }

        return null;
    }
}
