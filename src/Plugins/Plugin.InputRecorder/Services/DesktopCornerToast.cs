using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Plugin.InputRecorder.Services;

public enum DesktopCornerToastKind
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 作業領域右下に表示するトースト（長図キャプチャと同系統）。
/// </summary>
public static class DesktopCornerToast
{
    /// <summary>開始案内など短めの表示。</summary>
    public static readonly TimeSpan StartDisplayDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>終了・エラー案内の表示時間。</summary>
    public static readonly TimeSpan EndDisplayDuration = TimeSpan.FromSeconds(5);

    private static Window CreateWindow(string title, string body, DesktopCornerToastKind kind)
    {
        System.Windows.Media.Color bg = kind switch
        {
            DesktopCornerToastKind.Error => System.Windows.Media.Color.FromArgb(245, 160, 45, 45),
            DesktopCornerToastKind.Warning => System.Windows.Media.Color.FromArgb(245, 150, 95, 25),
            _ => System.Windows.Media.Color.FromArgb(245, 38, 38, 42)
        };

        var border = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(bg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = 400
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 236, 236)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        border.Child = sp;

        var w = new Window
        {
            Content = border,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight
        };

        w.Loaded += (_, _) =>
        {
            w.UpdateLayout();
            var wa = SystemParameters.WorkArea;
            w.Left = wa.Right - w.ActualWidth - 16;
            w.Top = wa.Bottom - w.ActualHeight - 16;
        };

        return w;
    }

    /// <summary>トーストを閉じるまで待つ（長図キャプチャの開始案内など）。</summary>
    public static async Task ShowAndWaitAsync(
        string title,
        string body,
        DesktopCornerToastKind kind,
        TimeSpan displayDuration,
        CancellationToken cancellationToken = default)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null) return;

        var closed = new TaskCompletionSource<bool>();

        await disp.InvokeAsync(() =>
        {
            var w = CreateWindow(title, body, kind);
            void SignalDone() => closed.TrySetResult(true);

            w.Closed += (_, _) => SignalDone();
            w.Show();
            var timer = new DispatcherTimer { Interval = displayDuration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                w.Close();
            };
            timer.Start();
        });

        await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>トーストを出して自動で閉じる（終了案内など）。</summary>
    public static void Show(string title, string body, DesktopCornerToastKind kind, TimeSpan autoClose)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null) return;

        void ShowCore()
        {
            var w = CreateWindow(title, body, kind);
            w.Show();
            var closeTimer = new DispatcherTimer { Interval = autoClose };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                w.Close();
            };
            closeTimer.Start();
        }

        if (disp.CheckAccess())
            ShowCore();
        else
            disp.BeginInvoke(ShowCore);
    }
}
