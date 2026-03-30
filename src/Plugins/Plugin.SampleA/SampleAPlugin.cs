using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleA;

public sealed class SampleAPlugin : IPluginModule
{
    public string Id => "sampleA";
    public string Title => "Excel Formatter";
    public string Description => "Batch apply presentation settings (Zoom, Selection, Landing Page) to existing workbooks.";
    public string IconKey => "📄";
    public int Order => 10;

    public UserControl CreateView()
    {
        return new SampleAView();
    }
}
