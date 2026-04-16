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

    private unsafe int FindScrollOffset(Bitmap prev, Bitmap curr)
    {
        int width = prev.Width;
        int height = prev.Height;
        
        BitmapData prevData = prev.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData currData = curr.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int bytesPerPixel = 4;
        int stride = prevData.Stride;
        
        byte* pPrev = (byte*)prevData.Scan0;
        byte* pCurr = (byte*)currData.Scan0;

        int sliceHeight = 100;
        if (height < 400) 
        {
            prev.UnlockBits(prevData);
            curr.UnlockBits(currData);
            return 0; 
        }

        int refY = height - 200 - sliceHeight; 
        
        int startX = width / 4;
        int endX = width * 3 / 4;
        int cmpWidth = endX - startX;

        int bestOffset = 0;
        int minDiff = int.MaxValue;

        for (int searchY = 0; searchY <= refY; searchY++)
        {
            int diff = 0;
            for (int y = 0; y < sliceHeight; y++)
            {
                byte* rPrev = pPrev + (refY + y) * stride + startX * bytesPerPixel;
                byte* rCurr = pCurr + (searchY + y) * stride + startX * bytesPerPixel;
                
                for (int x = 0; x < cmpWidth * bytesPerPixel; x += 4)
                {
                    diff += Math.Abs(rPrev[x] - rCurr[x]) + 
                            Math.Abs(rPrev[x+1] - rCurr[x+1]) + 
                            Math.Abs(rPrev[x+2] - rCurr[x+2]);
                            
                    if (diff > minDiff) break; 
                }
                if (diff > minDiff) break;
            }
            
            if (diff < minDiff)
            {
                minDiff = diff;
                bestOffset = refY - searchY; 
                if (diff == 0) break; 
            }
        }

        prev.UnlockBits(prevData);
        curr.UnlockBits(currData);

        int totalPixels = cmpWidth * sliceHeight;
        if (minDiff > totalPixels * 5) return 0; 

        return bestOffset;
    }
}
