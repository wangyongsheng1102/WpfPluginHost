using PuppeteerSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Plugin.InputRecorder.Services;

public class PuppeteerService
{
    public async Task CaptureFullPageAsync(string url, string outputPath, string? chromeExecutablePath = null)
    {
        var launchOptions = new LaunchOptions { Headless = true };
        
        if (!string.IsNullOrWhiteSpace(chromeExecutablePath) && File.Exists(chromeExecutablePath))
        {
            launchOptions.ExecutablePath = chromeExecutablePath;
        }
        else
        {
            using var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();
        }
        
        using var browser = await Puppeteer.LaunchAsync(launchOptions);
        using var page = await browser.NewPageAsync();
        
        // モバイル版のレイアウト崩れを防ぐため、標準的なデスクトップの解像度を使用する
        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
        
        // 動的コンテンツの読み込みを確実にするため、ネットワークがアイドル状態になるまで待機する
        await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);
        
        // フルページのレイアウトをキャプチャする
        await page.ScreenshotAsync(outputPath, new ScreenshotOptions { FullPage = true });
    }
}
