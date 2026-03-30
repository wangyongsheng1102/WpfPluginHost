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
        _themeService.ApplyTheme(isDarkTheme: true);

        _pluginManager = new PluginManager(pluginRoot);
        var vm = new MainWindowViewModel(_pluginManager, _themeService);
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
        // BaseDirectory 一般为 ...\ShellApp\bin\Debug\net8.0-windows\，需向上 5 级到仓库根（原为 4 级会落到不存在的 src\plugins）
        var workspacePlugins = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "plugins"));

        // 不能仅因目录存在就优先本地：空目录或无 deps 会阻断回退到仓库根 plugins
        if (IsUsablePluginFolder(localPlugins))
            return localPlugins;

        if (Directory.Exists(workspacePlugins))
            return workspacePlugins;

        Directory.CreateDirectory(localPlugins);
        return localPlugins;
    }

    /// <summary>
    /// 判断插件目录是否具备 AssemblyDependencyResolver 所需文件（Interop / Mvvm 等由宿主目录提供）。
    /// </summary>
    private static bool IsUsablePluginFolder(string path)
    {
        if (!Directory.Exists(path))
            return false;

        return Directory.GetFiles(path, "*.deps.json", SearchOption.TopDirectoryOnly).Length > 0;
    }
}
