using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.InputRecorder.Services;

public class LongScreenshotService
{
    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    public async Task CaptureLongScreenshotAsync(string outputPath, Action<string, int, bool>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        
        List<Bitmap> capturedParts = new List<Bitmap>();
        List<int> newHeights = new List<int>(); 

        Bitmap? prevBmp = null;
        int maxScrolls = 30;
        int maxNoOverlapCount = 0;

        try
        {
            for (int i = 0; i < maxScrolls; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                reportProgress?.Invoke($"スクロール＆キャプチャ中... ({i + 1}/{maxScrolls})", (i * 100 / maxScrolls), false);

                var bmp = new Bitmap(workArea.Width, workArea.Height, PixelFormat.Format32bppArgb);
                using (var gfx = Graphics.FromImage(bmp))
                {
                    gfx.CopyFromScreen(workArea.X, workArea.Y, 0, 0, workArea.Size, CopyPixelOperation.SourceCopy);
                }

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
                using var finalBmp = new Bitmap(workArea.Width, totalHeight, PixelFormat.Format32bppArgb);
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
            }
        }
        finally
        {
            foreach (var bmp in capturedParts) bmp.Dispose();
            reportProgress?.Invoke("長図キャプチャが完了しました", 100, false);
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
