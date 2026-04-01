using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.PostgreCompare;

public sealed class PostgreComparePlugin : IPluginModule
{
    public string Id => "postgreCompare";
    public string Title => "データ比較";
    public string Description => "PostgreSQL の DB設定・CSV比較・インポート/エクスポートを行います。";
    public string IconKey => "🗄️";
    public int Order => 30;

    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    public UserControl CreateView()
    {
        return new PostgreCompareView(_context);
    }
}

