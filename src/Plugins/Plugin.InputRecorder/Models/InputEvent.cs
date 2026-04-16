using System.Text.Json.Serialization;

namespace Plugin.InputRecorder.Models;

public enum InputEventType
{
    MouseMove,
    MouseDown,
    MouseUp,
    KeyDown,
    KeyUp
}

public class InputEvent
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InputEventType EventType { get; set; }
    
    // Elapsed milliseconds since the start of recording
    public long TimeOffset { get; set; }
    
    // For mouse events
    public int X { get; set; }
    public int Y { get; set; }
    
    // Virtual Key Code or Mouse Button identifier
    public int KeyCode { get; set; }
}
