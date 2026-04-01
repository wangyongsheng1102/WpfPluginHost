using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ShellApp.Services;

/// <summary>
/// 窗口图标：优先从输出目录的 Content 副本加载（避免 pack URI 对部分 .ico 解析异常），失败时再尝试程序集资源。
/// </summary>
internal static class AppIconLoader
{
    private const string IcoFileName = "toolbox_repair_box_tool_box_toolboxes_toolkit_icon_189326.ico";

    public static void ApplyTo(Window window)
    {
        var icon = TryLoad();
        if (icon is not null)
            window.Icon = icon;
    }

    private static BitmapFrame? TryLoad()
    {
        var baseDir = AppContext.BaseDirectory;
        var diskPath = Path.GetFullPath(Path.Combine(baseDir, "Assets", "Icons", IcoFileName));
        if (File.Exists(diskPath))
        {
            try
            {
                var uri = new Uri(diskPath, UriKind.Absolute);
                return BitmapFrame.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch
            {
                // fall through
            }
        }

        try
        {
            var packUri = new Uri($"pack://application:,,,/Assets/Icons/{IcoFileName}", UriKind.Absolute);
            return BitmapFrame.Create(packUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch
        {
            return null;
        }
    }
}
