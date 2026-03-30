using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ShellApp.Services;

/// <summary>
/// Application のテーマ用マージド辞書を差し替えた後、サブツリーで DynamicResource を再評価させる（プラグインアセンブリ内のビュー含む）。コントロールを作り直さず状態を保持する。
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

        // Popup などはメインのビジュアルツリーに未接続のことがあるが、Child は更新を試みる
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
