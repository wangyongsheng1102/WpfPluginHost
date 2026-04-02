using System.Reflection;
using Plugin.Abstractions;
using System.IO;

namespace ShellApp.Services;

public sealed class PluginManager : IDisposable
{
    private readonly string _pluginsRoot;
    private readonly string _shadowRoot;
    private readonly IPluginContext _pluginContext;
    private readonly object _sync = new();
    private readonly Dictionary<string, LoadedModule> _modulesByPath = new(StringComparer.OrdinalIgnoreCase);

    public PluginManager(string pluginsRoot, IPluginContext pluginContext)
    {
        _pluginsRoot = pluginsRoot;
        _pluginContext = pluginContext;
        _shadowRoot = Path.Combine(Path.GetTempPath(), "WpfPluginShell", "shadow");
        Directory.CreateDirectory(_pluginsRoot);
        Directory.CreateDirectory(_shadowRoot);
        ReloadAll();
    }

    public event EventHandler? PluginsChanged;

    public IReadOnlyCollection<LoadedModule> GetModules()
    {
        lock (_sync)
        {
            return _modulesByPath.Values.ToArray();
        }
    }

    public void ReloadAll()
    {
        lock (_sync)
        {
            UnloadInternal();

            // プラグイン本体の DLL のみを対象にする。依存 DLL（Interop、Mvvm 等）はメインと同じ出力にあり、GetTypes で走査しない
            foreach (var dllPath in EnumeratePluginAssemblyPaths(_pluginsRoot))
            {
                TryLoadDll(dllPath);
            }
        }

        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 1) <c>plugins/Plugin.XXX/Plugin.XXX.dll</c>（専用フォルダ＋依存を同梱）
    /// 2) 互換: <c>plugins/Plugin.XXX.dll</c>（フラット配置）
    /// </summary>
    private static IEnumerable<string> EnumeratePluginAssemblyPaths(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            yield break;
        }

        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(pluginsRoot))
        {
            var folderName = Path.GetFileName(dir);
            if (!folderName.StartsWith("Plugin.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = Path.Combine(dir, folderName + ".dll");
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(candidate), "Plugin.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            loadedIds.Add(Path.GetFileNameWithoutExtension(candidate)!);
            yield return candidate;
        }

        foreach (var dllPath in Directory.GetFiles(pluginsRoot, "Plugin.*.dll", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(dllPath), "Plugin.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(dllPath);
            if (id is not null && loadedIds.Contains(id))
            {
                continue;
            }

            yield return dllPath;
        }
    }

    private void TryLoadDll(string sourceDll)
    {
        try
        {
            var shadowDir = Path.Combine(_shadowRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(shadowDir);

            var sourceDir = Path.GetDirectoryName(sourceDll)!;
            // lib/ や言語フォルダ（ja 等）も含め、プラグインフォルダ全体をコピーしないと
            // AssemblyDependencyResolver が解決したパス先のファイルがシャドウに存在せず読み込みに失敗する。
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(shadowDir, relative);
                var destParent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destParent))
                {
                    Directory.CreateDirectory(destParent);
                }

                File.Copy(file, dest, overwrite: true);
            }

            var shadowDll = Path.Combine(shadowDir, Path.GetFileName(sourceDll));
            var loadContext = new PluginLoadContext(shadowDll);
            var assembly = loadContext.LoadFromAssemblyPath(shadowDll);

            var pluginType = assembly.GetTypes().FirstOrDefault(
                t => typeof(IPluginModule).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);
            if (pluginType is null)
            {
                loadContext.Unload();
                return;
            }

            if (Activator.CreateInstance(pluginType) is not IPluginModule module)
            {
                loadContext.Unload();
                return;
            }
            
            module.Initialize(_pluginContext);

            _modulesByPath[sourceDll] = new LoadedModule(sourceDll, module, loadContext, shadowDir, assembly);
        }
        catch
        {
            // 不正なプラグイン DLL は無視し、他の読み込みを続行する。
        }
    }

    private void UnloadInternal()
    {
        foreach (var loaded in _modulesByPath.Values)
        {
            loaded.LoadContext.Unload();
        }

        _modulesByPath.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            UnloadInternal();
        }
    }

    public sealed record LoadedModule(
        string SourcePath,
        IPluginModule Module,
        PluginLoadContext LoadContext,
        string ShadowDirectory,
        Assembly Assembly);
}
