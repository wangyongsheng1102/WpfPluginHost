using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleB;

public sealed class SampleBPlugin : IPluginModule
{
    public string Id => "sampleB";
    public string Title => "Sample B";
    public string Description => "Preview visual gallery components";
    public string IconKey => "🧩";
    public int Order => 20;

    public UserControl CreateView()
    {
        return new SampleBView();
    }
}
