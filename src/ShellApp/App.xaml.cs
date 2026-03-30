using System.Windows;
using System.IO;
using ShellApp.Services;
using ShellApp.ViewModels;
using ShellApp.Views;

namespace ShellApp;

public partial class App : Application
{
    private PluginManager? _pluginManager;
    private PluginWatcherService? _watcherService;
    private ThemeService? _themeService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pluginRoot = ResolvePluginRoot();
        Directory.CreateDirectory(pluginRoot);

        _themeService = new ThemeService();
        _themeService.ApplyTheme(isDarkTheme: false);

        var statusService = new GlobalStatusService();

        _pluginManager = new PluginManager(pluginRoot, statusService);
        var vm = new MainWindowViewModel(_pluginManager, _themeService, statusService);
        _watcherService = new PluginWatcherService(pluginRoot, _pluginManager);
        _watcherService.Start();

        var window = new MainWindow
        {
            DataContext = vm
        };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcherService?.Dispose();
        _pluginManager?.Dispose();
        base.OnExit(e);
    }

    private static string ResolvePluginRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var localPlugins = Path.Combine(baseDir, "plugins");
        // BaseDirectory は通常 ...\ShellApp\bin\Debug\net8.0-windows\。リポジトリ直下へは 5 段上がる（4 段だと存在しない src\plugins に落ちる）
        var workspacePlugins = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "plugins"));

        // ディレクトリが存在するだけではローカルを優先しない。空または deps が無いとリポジトリ直下の plugins へフォールバックできない
        if (IsUsablePluginFolder(localPlugins))
            return localPlugins;

        if (Directory.Exists(workspacePlugins))
            return workspacePlugins;

        Directory.CreateDirectory(localPlugins);
        return localPlugins;
    }

    /// <summary>
    /// プラグインフォルダに AssemblyDependencyResolver に必要なファイルがあるか（Interop / Mvvm 等はホスト側で提供）。
    /// </summary>
    private static bool IsUsablePluginFolder(string path)
    {
        if (!Directory.Exists(path))
            return false;

        return Directory.GetFiles(path, "*.deps.json", SearchOption.TopDirectoryOnly).Length > 0;
    }
}
