using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.InputRecorder;

public sealed class InputRecorderPlugin : IPluginModule
{
    public string Id => "inputRecorder";
    public string Title => "マクロ録画";
    public string Description => "マウスやキーボードの操作を記録し、自動実行（リプレイ）するプラグインです。";
    public string IconKey => "⏺️";
    public int Order => 30;

    private IPluginContext? _context;
    
    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    public System.Windows.Controls.UserControl CreateView()
    {
        return new InputRecorderView(_context);
    }
}
