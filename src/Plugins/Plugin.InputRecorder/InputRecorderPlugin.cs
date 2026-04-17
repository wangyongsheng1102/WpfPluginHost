using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.InputRecorder;

public sealed class InputRecorderPlugin : IPluginModule
{
    public string Id => "inputRecorder";
    public string Title => "マクロ録画";
    public string Description => "マウス・キーボード・ホイールを記録し、F9 で画面キャプチャ・F10 で長図を JSON に含めて保存・リプレイできます。";
    public string IconKey => "⏺️";
    public int Order => 50;

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
