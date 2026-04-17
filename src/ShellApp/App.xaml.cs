using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ShellApp.Services;
using ShellApp.ViewModels;
using ShellApp.Views;

namespace ShellApp;

public partial class App : Application
{
    private static readonly object StartupLogLock = new();
    private const string StartupLogFileName = "WPFPluginShell_startup_errors.log";

    private PluginManager? _pluginManager;
    private PluginWatcherService? _watcherService;
    private ThemeService? _themeService;
    private AppConfigService? _appConfigService;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionLogging();

        try
        {
            base.OnStartup(e);

            _appConfigService = new AppConfigService();

            var pluginRoot = ResolvePluginRoot();
            Directory.CreateDirectory(pluginRoot);

            _themeService = new ThemeService();
            _themeService.ApplyTheme(_appConfigService.Config.IsDarkTheme);

            var statusService = new GlobalStatusService(_appConfigService);

            _pluginManager = new PluginManager(pluginRoot, statusService);
            var vm = new MainWindowViewModel(_pluginManager, _themeService, statusService, _appConfigService);
            _watcherService = new PluginWatcherService(pluginRoot, _pluginManager);
            _watcherService.Start();

            var window = new MainWindow
            {
                DataContext = vm
            };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            TryAppendStartupLog("OnStartup", ex);
            try
            {
                MessageBox.Show(
                    "起動に失敗しました。詳細は exe と同じフォルダの " + StartupLogFileName + " を確認してください。\n\n" + ex.Message,
                    "WPFPluginShell",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // MessageBox も失敗する環境ではログのみ
            }

            Shutdown(1);
        }
    }

    private void RegisterGlobalExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            TryAppendStartupLog("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            try
            {
                MessageBox.Show(
                    "未処理の UI スレッド例外が発生しました。詳細は " + StartupLogFileName + " を参照してください。\n\n" + args.Exception.Message,
                    "WPFPluginShell",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }

            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                TryAppendStartupLog("UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TryAppendStartupLog("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void TryAppendStartupLog(string source, Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, StartupLogFileName);
            var text = $"[{DateTimeOffset.Now:u}] {source}\n{ex}\n\n";
            lock (StartupLogLock)
            {
                File.AppendAllText(path, text, Encoding.UTF8);
            }
        }
        catch
        {
        }
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

        return Directory.GetFiles(path, "*.deps.json", SearchOption.AllDirectories).Length > 0;
    }
}
