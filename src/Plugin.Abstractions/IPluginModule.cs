using System.Windows.Controls;

namespace Plugin.Abstractions;

public interface IPluginModule
{
    string Id { get; }
    string Title { get; }
    string IconKey { get; }
    int Order { get; }
    UserControl CreateView();
}
