using Plugin.Abstractions;
using Plugin.ReviewCheck.Views;

namespace Plugin.ReviewCheck;

public sealed class ReviewCheckPlugin : IPluginModule
{
    private IPluginContext? _context;

    public string Id => "reviewCheck";
    public string Title => "整合性チェック";
    public string Description => "成果物（SVN）と記入内容（Excel）をチェックします。";
    public string IconKey => "✅";
    public int Order => 35;

    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    public System.Windows.Controls.UserControl CreateView()
    {
        return new ReviewCheckView(_context);
    }
}
