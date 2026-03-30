using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ShellApp.Services;

/// <summary>
/// 在替换 Application 主题合并字典后，促使子树重新解析 DynamicResource（含插件程序集内视图），无需重建控件以免丢失状态。
/// </summary>
internal static class ThemeResourceRefresh
{
    public static void InvalidateThemeBoundProperties(DependencyObject root)
    {
        InvalidateRecursive(root);
    }

    private static void InvalidateRecursive(DependencyObject node)
    {
        if (node is FrameworkElement fe)
        {
            fe.InvalidateProperty(FrameworkElement.StyleProperty);
        }

        switch (node)
        {
            case Control c:
                c.InvalidateProperty(Control.BackgroundProperty);
                c.InvalidateProperty(Control.ForegroundProperty);
                c.InvalidateProperty(Control.BorderBrushProperty);
                break;
            case TextBlock tb:
                tb.InvalidateProperty(TextBlock.ForegroundProperty);
                tb.InvalidateProperty(TextBlock.BackgroundProperty);
                break;
            case Border b:
                b.InvalidateProperty(Border.BackgroundProperty);
                b.InvalidateProperty(Border.BorderBrushProperty);
                break;
            case Panel p:
                p.InvalidateProperty(Panel.BackgroundProperty);
                break;
        }

        // Popup 等可能尚未挂到主可视树，仍尝试刷新其 Child
        if (node is Popup popup && popup.Child is { } popupChild)
        {
            InvalidateRecursive(popupChild);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            InvalidateRecursive(VisualTreeHelper.GetChild(node, i));
        }
    }
}
