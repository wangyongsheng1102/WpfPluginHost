using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // Replay imports
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

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
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
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

    // Hook Structs
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

    public event Action? OnEscapePressed;
    public event Action<string>? OnScreenshotCaptured;
    public event Action? OnF10Pressed;

    public bool IsRecording { get; private set; }
    public bool IsReplaying { get; private set; }

    public IReadOnlyList<InputEvent> GetRecordedEvents() => _recordedEvents;

    public InputHookService()
    {
        _keyboardProc = KeyboardHookCallback;
        
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
            _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        }
    }

    public void StartRecording()
    {
        if (IsRecording || IsReplaying) return;

        _recordedEvents.Clear();
        _stopwatch = Stopwatch.StartNew();

        _mouseProc = MouseHookCallback;
        
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }

        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
            _mouseProc = null;
        }
        
        _stopwatch?.Stop();
        
        // 録画を停止したキーストローク（ESCなど）の削除は、通常UI側で処理されるためここでは行わない

        IsRecording = false;
    }

    public async Task ReplayAsync(IEnumerable<InputEvent> events, CancellationToken cancellationToken)
    {
        if (IsRecording || IsReplaying) return;
        IsReplaying = true;

        var sw = Stopwatch.StartNew();
        long lastTime = 0;

        foreach (var ev in events)
        {
            if (cancellationToken.IsCancellationRequested) break;

            long delay = ev.TimeOffset - lastTime;
            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
            }

            lastTime = ev.TimeOffset;
            SimulateEvent(ev);
        }

        sw.Stop();
        IsReplaying = false;
    }

    public void LoadEvents(IEnumerable<InputEvent> events)
    {
        _recordedEvents.Clear();
        _recordedEvents.AddRange(events);
    }

    private void SimulateEvent(InputEvent ev)
    {
        INPUT input = new INPUT();
        
        switch (ev.EventType)
        {
            case InputEventType.MouseMove:
                input.type = 0; // INPUT_MOUSE
                // SendInput用に絶対座標を正規化座標に変換する
                input.U.mi.dx = (ev.X * 65535) / GetSystemMetrics(0); // SM_CXSCREEN
                input.U.mi.dy = (ev.Y * 65535) / GetSystemMetrics(1); // SM_CYSCREEN
                input.U.mi.dwFlags = 0x8001; // MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE
                break;
            case InputEventType.MouseDown:
            case InputEventType.MouseUp:
                input.type = 0; // INPUT_MOUSE
                input.U.mi.dwFlags = GetMouseFlags(ev);
                break;
            case InputEventType.KeyDown:
            case InputEventType.KeyUp:
                input.type = 1; // INPUT_KEYBOARD
                input.U.ki.wVk = (ushort)ev.KeyCode;
                input.U.ki.dwFlags = ev.EventType == InputEventType.KeyUp ? 2U : 0U; // KEYEVENTF_KEYUP
                break;
        }

        SendInput(1, ref input, INPUT.Size);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private uint GetMouseFlags(InputEvent ev)
    {
        bool isDown = ev.EventType == InputEventType.MouseDown;
        switch (ev.KeyCode)
        {
            case 1: return isDown ? 0x0002U : 0x0004U; // LBUTTON
            case 2: return isDown ? 0x0008U : 0x0010U; // RBUTTON
            case 3: return isDown ? 0x0020U : 0x0040U; // MBUTTON
            default: return 0;
        }
    }

    private void TakeScreenshotAsync()
    {
        Task.Run(() =>
        {
            try
            {
                var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputRecord");
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                
                using var bmp = new System.Drawing.Bitmap(workArea.Width, workArea.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var gfx = System.Drawing.Graphics.FromImage(bmp);
                
                gfx.CopyFromScreen(workArea.X, workArea.Y, 0, 0, workArea.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                
                var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = System.IO.Path.Combine(folder, fileName);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                
                OnScreenshotCaptured?.Invoke(path);
            }
            catch
            {
                // フック実行中のキャプチャ失敗は安全に無視する
            }
        });
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            
            var ev = new InputEvent
            {
                TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0,
                X = hookStruct.pt.x,
                Y = hookStruct.pt.y
            };

            int wP = wParam.ToInt32();
            if (wP == WM_MOUSEMOVE)
            {
                ev.EventType = InputEventType.MouseMove;
                _recordedEvents.Add(ev);
            }
            else if (wP == WM_LBUTTONDOWN || wP == WM_RBUTTONDOWN || wP == WM_MBUTTONDOWN)
            {
                ev.EventType = InputEventType.MouseDown;
                ev.KeyCode = wP == WM_LBUTTONDOWN ? 1 : wP == WM_RBUTTONDOWN ? 2 : 3;
                _recordedEvents.Add(ev);
            }
            else if (wP == WM_LBUTTONUP || wP == WM_RBUTTONUP || wP == WM_MBUTTONUP)
            {
                ev.EventType = InputEventType.MouseUp;
                ev.KeyCode = wP == WM_LBUTTONUP ? 1 : wP == WM_RBUTTONUP ? 2 : 3;
                _recordedEvents.Add(ev);
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int wP = wParam.ToInt32();

            var ev = new InputEvent
            {
                TimeOffset = _stopwatch?.ElapsedMilliseconds ?? 0,
                KeyCode = (int)hookStruct.vkCode
            };

            if (hookStruct.vkCode == 121) // VK_F10
            {
                if (wP == WM_KEYDOWN || wP == WM_SYSKEYDOWN)
                {
                    OnF10Pressed?.Invoke();
                }
            }

            if (hookStruct.vkCode == 120) // VK_F9
            {
                if (wP == WM_KEYDOWN || wP == WM_SYSKEYDOWN)
                {
                    TakeScreenshotAsync();
                }
                // 他の操作でもF9を使用できるよう、入力をブロックせずパッシブにフックする
            }

            if (hookStruct.vkCode == 27 && IsRecording) // VK_ESCAPE
            {
                if (wP == WM_KEYDOWN || wP == WM_SYSKEYDOWN)
                {
                    OnEscapePressed?.Invoke();
                }
                return (IntPtr)1; // ESCキーの入力をブロック（無効化）する
            }

            if (IsRecording)
            {
                if (wP == WM_KEYDOWN || wP == WM_SYSKEYDOWN)
                {
                    ev.EventType = InputEventType.KeyDown;
                    _recordedEvents.Add(ev);
                }
                else if (wP == WM_KEYUP || wP == WM_SYSKEYUP)
                {
                    ev.EventType = InputEventType.KeyUp;
                    _recordedEvents.Add(ev);
                }
            }
        }
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        StopRecording();
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
            _keyboardProc = null;
        }
    }
}
