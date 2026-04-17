using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Plugin.InputRecorder.Models;

namespace Plugin.InputRecorder.Services;

public class InputHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint LLMHF_INJECTED = 0x00000001;

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;

    private readonly List<InputEvent> _recordedEvents = new();
    private Stopwatch? _stopwatch;

    private long _lastMoveSampleMs = -1;
    private int _lastMoveSampleX = int.MinValue;
    private int _lastMoveSampleY = int.MinValue;
    private bool _longScreenshotBusy;
    private string? _recordSessionFolder;
    private string? _replaySessionFolder;
    private int _recordImageIndex;
    private int _replayImageIndex;

    public event Action? OnEscapePressed;
    public event Action? OnReplayEscapePressed;
    public event Action<string>? OnScreenshotCaptured;
    /// <summary>録画中の F10。引数は既に <see cref="_recordedEvents"/> に追加済みのイベント（完了後に ExtraPath を埋める）。</summary>
    public event Action<InputEvent>? OnLongScreenshotRecordingRequested;

    public bool IsRecording { get; private set; }
    public bool IsReplaying { get; private set; }

    public IReadOnlyList<InputEvent> GetRecordedEvents() => new List<InputEvent>(_recordedEvents);

    public void NotifyLongScreenshotCaptureEnded() => _longScreenshotBusy = false;

    public void StartRecording()
    {
        if (IsRecording || IsReplaying) return;

        _recordedEvents.Clear();
        _recordSessionFolder = null;
        _recordImageIndex = 0;
        _stopwatch = Stopwatch.StartNew();
        _lastMoveSampleMs = -1;
        _lastMoveSampleX = int.MinValue;
        _lastMoveSampleY = int.MinValue;
        _longScreenshotBusy = false;

        _keyboardProc ??= KeyboardHookCallback;
        _mouseProc ??= MouseHookCallback;

        IntPtr moduleHandle = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        if (_keyboardHookId == IntPtr.Zero || _mouseHookId == IntPtr.Zero)
        {
            RemoveKeyboardHook();
            RemoveMouseHook();
            _stopwatch?.Stop();
            _stopwatch = null;
            return;
        }

        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        RemoveMouseHook();
        RemoveKeyboardHook();

        _stopwatch?.Stop();
        IsRecording = false;
    }

    public async Task ReplayAsync(IEnumerable<InputEvent> events, CancellationToken cancellationToken)
    {
        if (IsRecording || IsReplaying) return;

        _replaySessionFolder = null;
        _replayImageIndex = 0;
        _keyboardProc ??= KeyboardHookCallback;
        IntPtr moduleHandle = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

        IsReplaying = true;

        long lastTime = 0;
        long replayTimeCompensationMs = 0;
        try
        {
            foreach (var ev in events)
            {
                if (cancellationToken.IsCancellationRequested) break;

                long delay = ev.TimeOffset - lastTime;
                long adjustedDelay = Math.Max(0, delay - replayTimeCompensationMs);
                replayTimeCompensationMs = Math.Max(0, replayTimeCompensationMs - delay);
                if (adjustedDelay > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(adjustedDelay), cancellationToken).ConfigureAwait(false);

                lastTime = ev.TimeOffset;
                var started = Stopwatch.GetTimestamp();
                await SimulateEventAsync(ev, cancellationToken).ConfigureAwait(false);
                var elapsedMs = (long)Math.Round((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
                if (ev.EventType == InputEventType.LongScreenshot && elapsedMs > 0)
                    replayTimeCompensationMs += elapsedMs;
            }
        }
        finally
        {
            RemoveKeyboardHook();
            IsReplaying = false;
        }
    }

    public void LoadEvents(IEnumerable<InputEvent> events)
    {
        _recordedEvents.Clear();
        _recordedEvents.AddRange(events);
    }

    private static string EnsureInputRecordFolder()
    {
        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputRecord");
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        return folder;
    }

    private static string CreateSessionFolder(string suffix)
    {
        var baseFolder = EnsureInputRecordFolder();
        var sessionFolder = Path.Combine(baseFolder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}");
        Directory.CreateDirectory(sessionFolder);
        return sessionFolder;
    }

    private string EnsureRecordSessionFolder()
    {
        _recordSessionFolder ??= CreateSessionFolder("Record");
        return _recordSessionFolder;
    }

    private string EnsureReplaySessionFolder()
    {
        _replaySessionFolder ??= CreateSessionFolder("Replay");
        return _replaySessionFolder;
    }

    private string GenerateRecordImagePath()
    {
        var folder = EnsureRecordSessionFolder();
        var index = Interlocked.Increment(ref _recordImageIndex);
        return Path.Combine(folder, $"{index}.png");
    }

    private string GenerateReplayImagePath()
    {
        var folder = EnsureReplaySessionFolder();
        var index = Interlocked.Increment(ref _replayImageIndex);
        return Path.Combine(folder, $"{index}.png");
    }

    public string GenerateLongScreenshotPathForRecording()
    {
        return GenerateRecordImagePath();
    }

    private static System.Drawing.Rectangle ResolvePreferredWorkArea()
    {
        var mouseScreen = Screen.FromPoint(Control.MousePosition);
        if (mouseScreen is not null)
            return mouseScreen.WorkingArea;

        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            return Screen.FromHandle(hwnd).WorkingArea;

        return Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
    }

    /// <summary>鼠标所在屏幕优先，其次前台窗口所属屏幕，最后主屏兜底</summary>
    public static void CaptureActiveScreenWorkAreaToFile(string path)
    {
        var workArea = ResolvePreferredWorkArea();
        using var bmp = new System.Drawing.Bitmap(workArea.Width, workArea.Height, PixelFormat.Format32bppArgb);
        using var gfx = System.Drawing.Graphics.FromImage(bmp);
        gfx.CopyFromScreen(workArea.X, workArea.Y, 0, 0, workArea.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        bmp.Save(path, ImageFormat.Png);
    }

    private void EnqueueScreenshotEventAndCapture(string path)
    {
        _recordedEvents.Add(new InputEvent
        {
            EventType = InputEventType.Screenshot,
            TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0,
            ExtraPath = path
        });

        Task.Run(() =>
        {
            try
            {
                CaptureActiveScreenWorkAreaToFile(path);
                OnScreenshotCaptured?.Invoke(path);
            }
            catch
            {
                // フック経路では握りつぶし
            }
        });
    }

    private async Task SimulateEventAsync(InputEvent ev, CancellationToken cancellationToken)
    {
        switch (ev.EventType)
        {
            case InputEventType.MouseMove:
                SendMouseMoveAbsolute(ev.X, ev.Y);
                break;
            case InputEventType.MouseDown:
            case InputEventType.MouseUp:
                SendMouseMoveAbsolute(ev.X, ev.Y);
                SendMouseButton(ev.EventType, ev.KeyCode);
                break;
            case InputEventType.MouseWheel:
                SendMouseMoveAbsolute(ev.X, ev.Y);
                SendMouseWheel(ev.KeyCode);
                break;
            case InputEventType.KeyDown:
            case InputEventType.KeyUp:
                SendKey(ev.KeyCode, ev.EventType == InputEventType.KeyUp);
                break;
            case InputEventType.Screenshot:
            {
                var path = GenerateReplayImagePath();
                await Task.Run(() => CaptureActiveScreenWorkAreaToFile(path), cancellationToken).ConfigureAwait(false);
                break;
            }
            case InputEventType.LongScreenshot:
            {
                var path = GenerateReplayImagePath();
                var stitcher = new LongScreenshotService();
                await stitcher.CaptureLongScreenshotAsync(path, null, cancellationToken).ConfigureAwait(false);
                break;
            }
        }
    }

    private static void SendMouseMoveAbsolute(int x, int y)
    {
        int sw = Math.Max(1, GetSystemMetrics(0) - 1);
        int sh = Math.Max(1, GetSystemMetrics(1) - 1);
        int nx = (int)(x * 65535L / sw);
        int ny = (int)(y * 65535L / sh);
        var mi = new MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            mouseData = 0,
            dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        var input = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } };
        SendInput(1, ref input, INPUT.Size);
    }

    private static void SendMouseButton(InputEventType eventType, int button)
    {
        uint flags = button switch
        {
            1 => eventType == InputEventType.MouseDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            2 => eventType == InputEventType.MouseDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            3 => eventType == InputEventType.MouseDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
        if (flags == 0) return;

        var mi = new MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        var input = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } };
        SendInput(1, ref input, INPUT.Size);
    }

    private static void SendMouseWheel(int delta)
    {
        var mi = new MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = unchecked((uint)delta),
            dwFlags = MOUSEEVENTF_WHEEL,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        var input = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } };
        SendInput(1, ref input, INPUT.Size);
    }

    private static void SendKey(int virtualKey, bool keyUp)
    {
        var ki = new KEYBDINPUT
        {
            wVk = (ushort)virtualKey,
            wScan = 0,
            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = ki } };
        SendInput(1, ref input, INPUT.Size);
    }

    private bool ShouldSkipMouseMoveSample(int x, int y, long t)
    {
        if (_lastMoveSampleMs < 0) return false;
        long dt = t - _lastMoveSampleMs;
        int dx = Math.Abs(x - _lastMoveSampleX);
        int dy = Math.Abs(y - _lastMoveSampleY);
        return dx < 4 && dy < 4 && dt < 25;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int wP = wParam.ToInt32();
            long t = _stopwatch?.ElapsedMilliseconds ?? 0;

            if (wP == WM_MOUSEMOVE)
            {
                if (!ShouldSkipMouseMoveSample(hookStruct.pt.x, hookStruct.pt.y, t))
                {
                    _lastMoveSampleMs = t;
                    _lastMoveSampleX = hookStruct.pt.x;
                    _lastMoveSampleY = hookStruct.pt.y;
                    _recordedEvents.Add(new InputEvent
                    {
                        EventType = InputEventType.MouseMove,
                        TimeOffset = t,
                        X = hookStruct.pt.x,
                        Y = hookStruct.pt.y
                    });
                }
            }
            else if (wP is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                _lastMoveSampleMs = t;
                _lastMoveSampleX = hookStruct.pt.x;
                _lastMoveSampleY = hookStruct.pt.y;
                _recordedEvents.Add(new InputEvent
                {
                    EventType = InputEventType.MouseDown,
                    TimeOffset = t,
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y,
                    KeyCode = wP == WM_LBUTTONDOWN ? 1 : wP == WM_RBUTTONDOWN ? 2 : 3
                });
            }
            else if (wP is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP)
            {
                _lastMoveSampleMs = t;
                _lastMoveSampleX = hookStruct.pt.x;
                _lastMoveSampleY = hookStruct.pt.y;
                _recordedEvents.Add(new InputEvent
                {
                    EventType = InputEventType.MouseUp,
                    TimeOffset = t,
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y,
                    KeyCode = wP == WM_LBUTTONUP ? 1 : wP == WM_RBUTTONUP ? 2 : 3
                });
            }
            else if (wP == WM_MOUSEWHEEL)
            {
                // 长图截图期间会通过 mouse_event 注入滚轮；这些内部事件不应污染录制脚本
                if (_longScreenshotBusy && (hookStruct.flags & LLMHF_INJECTED) != 0)
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

                _lastMoveSampleMs = t;
                _lastMoveSampleX = hookStruct.pt.x;
                _lastMoveSampleY = hookStruct.pt.y;
                short delta = (short)(hookStruct.mouseData >> 16);
                _recordedEvents.Add(new InputEvent
                {
                    EventType = InputEventType.MouseWheel,
                    TimeOffset = t,
                    X = hookStruct.pt.x,
                    Y = hookStruct.pt.y,
                    KeyCode = delta
                });
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int wP = wParam.ToInt32();

            if (IsRecording)
            {
                if (hookStruct.vkCode == 121) // VK_F10
                {
                    if (wP is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (!_longScreenshotBusy)
                        {
                            _longScreenshotBusy = true;
                            var pending = new InputEvent
                            {
                                EventType = InputEventType.LongScreenshot,
                                TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0
                            };
                            _recordedEvents.Add(pending);
                            OnLongScreenshotRecordingRequested?.Invoke(pending);
                        }
                    }
                }
                else if (hookStruct.vkCode == 120) // VK_F9
                {
                    if (wP is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        var path = GenerateRecordImagePath();
                        EnqueueScreenshotEventAndCapture(path);
                    }
                }
                else if (hookStruct.vkCode == 27) // VK_ESCAPE（KeyDown のみブロックし、KeyUp は通す）
                {
                    if (wP is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        OnEscapePressed?.Invoke();
                        return (IntPtr)1;
                    }
                }
                else
                {
                    if (wP is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        _recordedEvents.Add(new InputEvent
                        {
                            EventType = InputEventType.KeyDown,
                            TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0,
                            KeyCode = (int)hookStruct.vkCode
                        });
                    }
                    else if (wP is WM_KEYUP or WM_SYSKEYUP)
                    {
                        _recordedEvents.Add(new InputEvent
                        {
                            EventType = InputEventType.KeyUp,
                            TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0,
                            KeyCode = (int)hookStruct.vkCode
                        });
                    }
                }
            }
            else if (IsReplaying)
            {
                if (hookStruct.vkCode == 27 && (wP is WM_KEYDOWN or WM_SYSKEYDOWN) && (hookStruct.flags & LLKHF_INJECTED) == 0)
                {
                    OnReplayEscapePressed?.Invoke();
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHookId);
        _mouseHookId = IntPtr.Zero;
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_keyboardHookId);
        _keyboardHookId = IntPtr.Zero;
    }

    public void Dispose() => StopRecording();
}
