using System.IO;
using System.Windows.Input;

namespace Plugin.InputRecorder.Models;

/// <summary>
/// イベント一覧用の表示文字列。録画 JSON・リプレイは <see cref="InputEvent.KeyCode"/> 等の生値のみを使用する。
/// </summary>
public static class InputEventDisplay
{
    public static string FormatKeyOrAction(InputEvent e)
    {
        return e.EventType switch
        {
            InputEventType.MouseMove => "移動",
            InputEventType.MouseDown => FormatMouseButton(e.KeyCode, down: true),
            InputEventType.MouseUp => FormatMouseButton(e.KeyCode, down: false),
            InputEventType.MouseWheel => FormatWheel(e.KeyCode),
            InputEventType.KeyDown => $"{FormatVirtualKey(e.KeyCode)} Down",
            InputEventType.KeyUp => $"{FormatVirtualKey(e.KeyCode)} Up",
            InputEventType.Screenshot => FormatScreenshot(e),
            InputEventType.LongScreenshot => FormatLongScreenshot(e),
            _ => "—"
        };
    }

    private static string FormatMouseButton(int code, bool down)
    {
        var name = code switch
        {
            1 => "左",
            2 => "右",
            3 => "中",
            _ => $"#{code}"
        };
        return down ? $"{name}ボタン Down" : $"{name}ボタン Up";
    }

    private static string FormatWheel(int delta)
    {
        if (delta == 0)
            return "ホイール";
        return delta > 0
            ? $"ホイール上 (+{delta})"
            : $"ホイール下 ({delta})";
    }

    private static string FormatVirtualKey(int vk)
    {
        try
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            if (key == Key.None)
                return $"VK 0x{vk:X2}";

            var s = key.ToString();
            // WPF Key: D0–D9 → 数字表記
            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]))
                return s[1].ToString();
            return s;
        }
        catch
        {
            return $"VK 0x{vk:X2}";
        }
    }

    private static string FormatScreenshot(InputEvent e)
    {
        if (string.IsNullOrEmpty(e.ExtraPath))
            return "画面キャプチャ (F9)";
        return $"画面キャプチャ: {Path.GetFileName(e.ExtraPath)}";
    }

    private static string FormatLongScreenshot(InputEvent e)
    {
        if (string.IsNullOrEmpty(e.ExtraPath))
            return "長図キャプチャ (F10)";
        return $"長図: {Path.GetFileName(e.ExtraPath)}";
    }
}
