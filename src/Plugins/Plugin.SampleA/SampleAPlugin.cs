using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleA;

public sealed class SampleAPlugin : IPluginModule
{
    public string Id => "sampleA";
    public string Title => "Excel 書式設定";
    public string Description => "既存ブックに表示倍率・アクティブセル・表示シートなどの表示設定を一括適用します。";
    public string IconKey => "📄";
    public int Order => 10;

    public UserControl CreateView()
    {
        return new SampleAView();
    }
}
