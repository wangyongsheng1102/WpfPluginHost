using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleA;

public sealed class SampleAPlugin : IPluginModule
{
    public string Id => "excelFormatter";
    public string Title => "ExcelFormatter";
    public string Description => "既存ブックに表示倍率・カーソル位置・印刷設定など各種書式を一括適用します。機能ごとにチェックボックスで個別選択できます。";
    public string IconKey => "📄";
    public int Order => 10;

    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    public UserControl CreateView()
    {
        return new SampleAView(_context);
    }
}
