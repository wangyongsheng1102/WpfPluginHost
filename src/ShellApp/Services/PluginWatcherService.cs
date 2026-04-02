using System.IO;

namespace ShellApp.Services;

public sealed class PluginWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly PluginManager _pluginManager;
    private readonly System.Timers.Timer _debounceTimer;

    public PluginWatcherService(string pluginsRoot, PluginManager pluginManager)
    {
        _pluginManager = pluginManager;

        _watcher = new FileSystemWatcher(pluginsRoot, "*.dll")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        _watcher.Created += (_, _) => TriggerReload();
        _watcher.Changed += (_, _) => TriggerReload();
        _watcher.Deleted += (_, _) => TriggerReload();
        _watcher.Renamed += (_, _) => TriggerReload();

        _debounceTimer = new System.Timers.Timer(500)
        {
            AutoReset = false,
            Enabled = false
        };
        _debounceTimer.Elapsed += (_, _) => _pluginManager.ReloadAll();
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    private void TriggerReload()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
