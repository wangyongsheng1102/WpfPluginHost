using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Plugin.InputRecorder.Services;

public class LongScreenshotService
{
    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>
    /// 主作業領域と前面ウィンドウの交差を取り、ブラウザ等のクライアント付近だけを切り出す（取得できない場合は作業領域全体）。
    /// </summary>
    private static Rectangle ResolveCaptureRectangle(Rectangle workArea)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var wr))
            return workArea;

        var windowRect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var inter = Rectangle.Intersect(windowRect, workArea);
        if (inter.Width < 200 || inter.Height < 200)
            return workArea;

        return inter;
    }

    /// <summary>
    /// 縦スクロール結合で毎フレーム重なる右端・下端のスクロールバー帯を除去する（システム標準幅＋数 px）。
    /// </summary>
    private static Bitmap CropScrollbarChrome(Bitmap src)
    {
        int stripRight = SystemInformation.VerticalScrollBarWidth + 3;
        int stripBottom = SystemInformation.HorizontalScrollBarHeight + 3;
        stripRight = Math.Min(stripRight, Math.Max(0, src.Width - 1));
        stripBottom = Math.Min(stripBottom, Math.Max(0, src.Height - 1));
        if (stripRight == 0 && stripBottom == 0)
            return src;

        int nw = src.Width - stripRight;
        int nh = src.Height - stripBottom;
        if (nw < 16 || nh < 16)
            return src;

        var dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
            g.DrawImage(src, 0, 0, new Rectangle(0, 0, nw, nh), GraphicsUnit.Pixel);
        src.Dispose();
        return dst;
    }

    /// <summary>
    /// トレイのバルーン通知（<see cref="NotifyIcon.ShowBalloonTip"/>）。
    /// キャプチャはバックグラウンドスレッドから呼ばれることが多いため、WPF の Dispatcher 上で表示する。
    /// </summary>
    private static void ShowTrayBalloon(string title, string text, ToolTipIcon icon)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null) return;

        void ShowCore()
        {
            var ni = new NotifyIcon
            {
                Visible = true,
                Icon = icon == ToolTipIcon.Error ? SystemIcons.Error : SystemIcons.Information,
                Text = title
            };
            ni.ShowBalloonTip(5000, title, text, icon);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            EventHandler? tick = null;
            tick = (_, _) =>
            {
                timer.Stop();
                timer.Tick -= tick!;
                ni.Visible = false;
                ni.Dispose();
            };
            timer.Tick += tick;
            timer.Start();
        }

        if (disp.CheckAccess())
            ShowCore();
        else
            disp.BeginInvoke(ShowCore);
    }

    public async Task CaptureLongScreenshotAsync(string outputPath, Action<string, int, bool>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var capRect = ResolveCaptureRectangle(workArea);

        List<Bitmap> capturedParts = new List<Bitmap>();
        List<int> newHeights = new List<int>();

        Bitmap? prevBmp = null;
        int maxScrolls = 30;
        int maxNoOverlapCount = 0;
        var cancelled = false;
        var saved = false;
        var endBalloonHandled = false;

        try
        {
            ShowTrayBalloon(
                "長図キャプチャ",
                "開始しました。前面ウィンドウ付近を切り出し、スクロールバー帯を除いて結合します。しばらくお待ちください。",
                ToolTipIcon.Info);

            try
            {
                for (int i = 0; i < maxScrolls; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    reportProgress?.Invoke($"スクロール＆キャプチャ中... ({i + 1}/{maxScrolls})", (i * 100 / maxScrolls), false);

                    var bmp = new Bitmap(capRect.Width, capRect.Height, PixelFormat.Format32bppArgb);
                    using (var gfx = Graphics.FromImage(bmp))
                    {
                        gfx.CopyFromScreen(capRect.X, capRect.Y, 0, 0, capRect.Size, CopyPixelOperation.SourceCopy);
                    }

                    bmp = CropScrollbarChrome(bmp);

                    if (prevBmp == null)
                    {
                        capturedParts.Add(bmp);
                        newHeights.Add(bmp.Height);
                        prevBmp = bmp;
                    }
                    else
                    {
                        int scrollOffset = FindScrollOffset(prevBmp, bmp);

                        if (scrollOffset <= 0)
                        {
                            maxNoOverlapCount++;
                            if (maxNoOverlapCount >= 2)
                            {
                                bmp.Dispose();
                                break;
                            }
                        }
                        else
                        {
                            maxNoOverlapCount = 0;
                            capturedParts.Add(bmp);
                            newHeights.Add(scrollOffset);
                            prevBmp = bmp;
                        }
                    }

                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -300, 0);
                    await Task.Delay(400, cancellationToken);
                }

                reportProgress?.Invoke("画像を結合しています...", 90, true);

                int totalHeight = 0;
                foreach (var h in newHeights) totalHeight += h;

                if (totalHeight > 0)
                {
                    int outW = capturedParts[0].Width;
                    using var finalBmp = new Bitmap(outW, totalHeight, PixelFormat.Format32bppArgb);
                    using var finalGfx = Graphics.FromImage(finalBmp);

                    int currentY = 0;
                    for (int i = 0; i < capturedParts.Count; i++)
                    {
                        var part = capturedParts[i];
                        int addedHeight = newHeights[i];

                        if (i == 0)
                        {
                            finalGfx.DrawImageUnscaled(part, 0, 0);
                            currentY += part.Height;
                        }
                        else
                        {
                            Rectangle srcRect = new Rectangle(0, part.Height - addedHeight, part.Width, addedHeight);
                            finalGfx.DrawImage(part, 0, currentY, srcRect, GraphicsUnit.Pixel);
                            currentY += addedHeight;
                        }
                    }

                    finalBmp.Save(outputPath, ImageFormat.Png);
                    saved = true;
                }
            }
            catch (Exception ex)
            {
                endBalloonHandled = true;
                ShowTrayBalloon("長図キャプチャ", $"エラーにより終了しました。{ex.Message}", ToolTipIcon.Error);
                throw;
            }
        }
        finally
        {
            foreach (var bmp in capturedParts) bmp.Dispose();
            reportProgress?.Invoke("長図キャプチャが完了しました", 100, false);

            if (!endBalloonHandled)
            {
                if (saved)
                    ShowTrayBalloon("長図キャプチャ", "終了しました。長図を保存しました。", ToolTipIcon.Info);
                else if (cancelled)
                    ShowTrayBalloon("長図キャプチャ", "終了しました。キャンセルされました。", ToolTipIcon.Warning);
                else
                    ShowTrayBalloon(
                        "長図キャプチャ",
                        "終了しました。スクロールの重なりが十分でないため、長図は保存されませんでした。",
                        ToolTipIcon.Warning);
            }
        }
    }

    /// <summary>
    /// 前フレームと現フレームの縦スクロール量を、中央帯の画素差分で推定する。
    /// LockBits 後は <see cref="Marshal.Copy"/> でマネージ配列へ取り込み、unsafe なしで同じ比較を行う。
    /// </summary>
    private static int FindScrollOffset(Bitmap prev, Bitmap curr)
    {
        int width = prev.Width;
        int height = prev.Height;
        const int bytesPerPixel = 4;
        const int sliceHeight = 100;

        if (height < 400 || width < 16)
            return 0;

        var prevData = prev.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currData = curr.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowStride = Math.Abs(prevData.Stride);
            if (Math.Abs(currData.Stride) != rowStride)
                return 0;

            int len = rowStride * height;
            var prevBytes = GC.AllocateUninitializedArray<byte>(len);
            var currBytes = GC.AllocateUninitializedArray<byte>(len);
            Marshal.Copy(prevData.Scan0, prevBytes, 0, len);
            Marshal.Copy(currData.Scan0, currBytes, 0, len);

            int refY = height - 200 - sliceHeight;
            int startX = width / 4;
            int endX = width * 3 / 4;
            int cmpWidth = endX - startX;
            int startOffset = startX * bytesPerPixel;
            int rowCmpBytes = cmpWidth * bytesPerPixel;

            int bestOffset = 0;
            int minDiff = int.MaxValue;

            for (int searchY = 0; searchY <= refY; searchY++)
            {
                int diff = 0;
                for (int y = 0; y < sliceHeight; y++)
                {
                    int iPrev = (refY + y) * rowStride + startOffset;
                    int iCurr = (searchY + y) * rowStride + startOffset;
                    for (int xb = 0; xb < rowCmpBytes; xb += 4)
                    {
                        diff += Math.Abs(prevBytes[iPrev + xb] - currBytes[iCurr + xb]) +
                                Math.Abs(prevBytes[iPrev + xb + 1] - currBytes[iCurr + xb + 1]) +
                                Math.Abs(prevBytes[iPrev + xb + 2] - currBytes[iCurr + xb + 2]);
                        if (diff > minDiff)
                            break;
                    }

                    if (diff > minDiff)
                        break;
                }

                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestOffset = refY - searchY;
                    if (diff == 0)
                        break;
                }
            }

            int totalPixels = cmpWidth * sliceHeight;
            if (minDiff > totalPixels * 5)
                return 0;

            return bestOffset;
        }
        finally
        {
            prev.UnlockBits(prevData);
            curr.UnlockBits(currData);
        }
    }
}
