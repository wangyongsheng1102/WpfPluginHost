using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.PixelCompare;

public sealed class PixelComparePlugin : IPluginModule
{
    public string Id => "pixelCompare";
    public string Title => "Pixel Compare";
    public string Description => "Excel に埋め込まれた画像を列指定で比較し、差分を赤枠で可視化します。";
    public string IconKey => "🧪";
    public int Order => 40;

    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    public UserControl CreateView()
    {
        return new PixelCompareView(_context);
    }
}
