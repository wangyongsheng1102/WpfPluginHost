using System.IO;
using System.Windows.Input;

namespace Plugin.InputRecorder.Models;

/// <summary>
/// InputEvent のリードオンリー表示ラッパー。XAML バインディング用。
/// イベント一覧の各行に対応し、フォーマット済みの文字列をプロパティとして公開する。
/// </summary>
public sealed class InputEventDisplay
{
    public InputEventDisplay(InputEvent source)
    {
        EventType = source.EventType.ToString();
        TimeOffset = source.TimeOffset;
        X = source.X;
        Y = source.Y;
        KeyCode = source.KeyCode;
        KeyOrActionDescription = FormatKeyOrAction(source);
    }

    public string EventType { get; }
    public long TimeOffset { get; }
    public int X { get; }
    public int Y { get; }
    public int KeyCode { get; }
    public string KeyOrActionDescription { get; }

    private static string FormatKeyOrAction(InputEvent e)
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
        if (delta == 0) return "ホイール";
        return delta > 0 ? $"ホイール上 (+{delta})" : $"ホイール下 ({delta})";
    }

    private static string FormatVirtualKey(int vk)
    {
        try
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            if (key == Key.None)
                return $"VK 0x{vk:X2}";
            var s = key.ToString();
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
        return string.IsNullOrEmpty(e.ExtraPath) ? "画面キャプチャ (F9)" : $"画面キャプチャ: {Path.GetFileName(e.ExtraPath)}";
    }

    private static string FormatLongScreenshot(InputEvent e)
    {
        return string.IsNullOrEmpty(e.ExtraPath) ? "長図キャプチャ (F10)" : $"長図: {Path.GetFileName(e.ExtraPath)}";
    }
}
