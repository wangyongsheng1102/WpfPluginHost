using System.Text.Json.Serialization;

namespace Plugin.InputRecorder.Models;

public enum InputEventType
{
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel,
    KeyDown,
    KeyUp,
    /// <summary>F9 全画面（作業領域）キャプチャ。ExtraPath に保存先。</summary>
    Screenshot,
    /// <summary>F10 長図キャプチャ開始タイミング。ExtraPath に結合後の PNG パス。</summary>
    LongScreenshot,
}

public class InputEvent
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InputEventType EventType { get; set; }

    /// <summary>録画開始からの経過ミリ秒</summary>
    public long TimeOffset { get; set; }

    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>
    /// キー: 仮想キーコード。マウスボタン: 1=左,2=右,3=中。
    /// ホイール: WM_MOUSEWHEEL の符号付きデルタ（通常 ±120 の倍数）。
    /// </summary>
    public int KeyCode { get; set; }

    /// <summary>スクリーンショット・長図の保存パス（録画完了時点で確定）</summary>
    public string? ExtraPath { get; set; }
}
