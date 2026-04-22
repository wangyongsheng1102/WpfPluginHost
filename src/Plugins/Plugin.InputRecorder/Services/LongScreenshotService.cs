using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Plugin.InputRecorder.Services;

public class LongScreenshotService
{
    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>前面ウィンドウと作業領域の交差。スクロールバーがビットマップ端に寄るので右下を裁ちやすい。</summary>
    private static Rectangle ResolveCaptureRectangle(Rectangle workArea)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            hwnd = GetDesktopWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var wr))
            return workArea;

        var windowRect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var inter = Rectangle.Intersect(windowRect, workArea);
        if (inter.Width < 200 || inter.Height < 200)
            return workArea;

        return inter;
    }

    private static Rectangle ResolvePreferredWorkArea()
    {
        var mouseScreen = Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        if (mouseScreen is not null)
            return mouseScreen.WorkingArea;

        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            return Screen.FromHandle(hwnd).WorkingArea;

        return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
    }

    /// <summary>縦結合で毎フレーム重なる右端・下端のスクロールバー帯を除去する。</summary>
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

        var dst = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
            g.DrawImage(src, 0, 0, new Rectangle(0, 0, nw, nh), GraphicsUnit.Pixel);
        src.Dispose();
        return dst;
    }

    public async Task CaptureLongScreenshotAsync(string outputPath, Action<string, int, bool>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        var workArea = ResolvePreferredWorkArea();
        // 1 周目で決めた矩形を固定する（毎フレームサイズが変わると重なり検出が不安定になる）
        Rectangle lockedCaptureRect = default;
        var haveLockedCaptureRect = false;

        List<Bitmap> capturedParts = new List<Bitmap>();
        List<int> newHeights = new List<int>();

        Bitmap? prevBmp = null;
        int maxScrolls = 48;
        int maxNoOverlapCount = 0;
        const int maxNoOverlapBeforeStop = 5;
        var cancelled = false;
        var saved = false;
        var endBalloonHandled = false;

        try
        {
            await DesktopCornerToast.ShowAndWaitAsync(
                    "長図キャプチャ",
                    "開始します。このメッセージが閉じたあと、前面ウィンドウ付近を切り出し、スクロールバーを除いてスクロール結合します。",
                    DesktopCornerToastKind.Info,
                    DesktopCornerToast.StartDisplayDuration,
                    cancellationToken)
                .ConfigureAwait(false);

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

                    if (!haveLockedCaptureRect)
                    {
                        lockedCaptureRect = ResolveCaptureRectangle(workArea);
                        haveLockedCaptureRect = true;
                    }

                    var capRect = lockedCaptureRect;
                    var bmp = new Bitmap(capRect.Width, capRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
                            bmp.Dispose();
                            if (maxNoOverlapCount >= maxNoOverlapBeforeStop)
                                break;
                        }
                        else
                        {
                            maxNoOverlapCount = 0;
                            capturedParts.Add(bmp);
                            newHeights.Add(scrollOffset);
                            prevBmp = bmp;
                        }
                    }

                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -360, 0);
                    await Task.Delay(520, cancellationToken);
                }

                reportProgress?.Invoke("画像を結合しています...", 90, true);

                int totalHeight = 0;
                foreach (var h in newHeights) totalHeight += h;

                if (totalHeight > 0)
                {
                    int outW = capturedParts[0].Width;
                    using var finalBmp = new Bitmap(outW, totalHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
                DesktopCornerToast.Show("長図キャプチャ", $"エラーにより終了しました。{ex.Message}", DesktopCornerToastKind.Error, DesktopCornerToast.EndDisplayDuration);
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
                    DesktopCornerToast.Show("長図キャプチャ", "終了しました。長図を保存しました。", DesktopCornerToastKind.Info, DesktopCornerToast.EndDisplayDuration);
                else if (cancelled)
                    DesktopCornerToast.Show("長図キャプチャ", "終了しました。キャンセルされました。", DesktopCornerToastKind.Warning, DesktopCornerToast.EndDisplayDuration);
                else
                    DesktopCornerToast.Show(
                        "長図キャプチャ",
                        "終了しました。スクロールの重なりが十分でないため、長図は保存されませんでした。",
                        DesktopCornerToastKind.Warning,
                        DesktopCornerToast.EndDisplayDuration);
            }
        }
    }

    /// <summary>
    /// 前フレームと現フレームの縦スクロール量を、中央帯の画素差分で推定する。
    /// Parallel.For で検索範囲を並列化し、共有の最小差分で早期打ち切りを行う。
    /// </summary>
    private static int FindScrollOffset(Bitmap prev, Bitmap curr)
    {
        int width = prev.Width;
        int height = prev.Height;
        const int bytesPerPixel = 4;
        const int sliceHeight = 100;

        if (height < 400 || width < 16)
            return 0;

        var prevData = prev.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var currData = curr.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
            int globalMinDiff = int.MaxValue;
            var lockObj = new object();

            // Pre-compute reference slice row offsets
            var refRowOffsets = new int[sliceHeight];
            for (int y = 0; y < sliceHeight; y++)
                refRowOffsets[y] = (refY + y) * rowStride + startOffset;

            Parallel.For(0, refY + 1, (searchY, loopState) =>
            {
                var currentMin = Volatile.Read(ref globalMinDiff);
                int diff = 0;
                for (int y = 0; y < sliceHeight; y++)
                {
                    int iPrev = refRowOffsets[y];
                    int iCurr = (searchY + y) * rowStride + startOffset;
                    for (int xb = 0; xb < rowCmpBytes; xb += 4)
                    {
                        diff += Math.Abs(prevBytes[iPrev + xb] - currBytes[iCurr + xb]) +
                                Math.Abs(prevBytes[iPrev + xb + 1] - currBytes[iCurr + xb + 1]) +
                                Math.Abs(prevBytes[iPrev + xb + 2] - currBytes[iCurr + xb + 2]);
                        if (diff > currentMin)
                            goto nextSearch;
                    }

                    if (diff > currentMin)
                        goto nextSearch;
                }

                lock (lockObj)
                {
                    if (diff < globalMinDiff)
                    {
                        globalMinDiff = diff;
                        bestOffset = refY - searchY;
                        if (diff == 0)
                            loopState.Stop();
                    }
                }
                nextSearch:;
            });

            int totalPixels = cmpWidth * sliceHeight;
            if (globalMinDiff > totalPixels * 5)
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
