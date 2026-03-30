using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleB;

public sealed class SampleBPlugin : IPluginModule
{
    public string Id => "sampleB";
    public string Title => "サンプル B";
    public string Description => "画像を選択してプレビューするサンプルです。";
    public string IconKey => "🧩";
    public int Order => 20;

    public void Initialize(IPluginContext context)
    {
    }

    public UserControl CreateView()
    {
        return new SampleBView();
    }
}
