using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class QuickCaptureHotkey : IDisposable
{
    private const int FirstId = 0x4C56, SecondId = 0x4C57;
    private sealed record Registration(QuickCaptureShortcut Shortcut, int Id, IntPtr Hook);
    private readonly Dispatcher _dispatcher;
    private readonly Action _trigger;
    private readonly HwndSource _source;
    private readonly KeyboardProc _callback, _recordCallback;
    private Registration? _active;
    private bool _held, _disposed;
    private IntPtr _recordHook, _recordWindow;
    private Action<QuickCaptureShortcut?>? _recorded;
    private int _recordKey;
    private QuickCaptureShortcut? _recordedShortcut;

    public QuickCaptureHotkey(Dispatcher dispatcher, Action trigger)
    {
        _dispatcher = dispatcher; _trigger = trigger; _callback = OnKeyboard;
        _recordCallback = OnRecordKey;
        _source = new HwndSource(new HwndSourceParameters("Levitation.QuickCapture") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
    }
    public string? Apply(QuickCaptureShortcut? shortcut, bool allowWidgetsFallback, Action<Action>? save = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = _active;
        if (previous?.Shortcut == shortcut && (previous == null || previous.Hook == IntPtr.Zero || allowWidgetsFallback))
        {
            save?.Invoke(() => { });
            return null;
        }

        Registration? candidate = null;
        if (shortcut is { } key)
        {
            if (!key.IsValid) return "快捷键格式无效，请重新录制或恢复默认。";
            int id = previous?.Id == FirstId ? SecondId : FirstId;
            if (RegisterHotKey(_source.Handle, id, (uint)key.Modifiers | 0x4000, (uint)key.VirtualKey))
                candidate = new Registration(key, id, IntPtr.Zero);
            else if (allowWidgetsFallback && key == QuickCaptureShortcut.Default)
            {
                // The built-in shortcut can replace Windows Widgets. Recorded shortcuts use RegisterHotKey only.
                var hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) return "无法启用默认快捷键。";
                candidate = new Registration(key, 0, hook);
            }
            else
                return "此组合键已被占用或由 Windows 保留。";
        }

        void Activate()
        {
            _active = candidate;
            _held = false;
            Release(previous);
        }
        if (save == null) Activate();
        else
        {
            try { save(Activate); }
            catch
            {
                // An observer can throw after the settings were saved and the candidate activated.
                if (!ReferenceEquals(_active, candidate)) Release(candidate);
                throw;
            }
        }
        return null;
    }

    public bool BeginRecording(IntPtr window, Action<QuickCaptureShortcut?> recorded)
    {
        EndRecording();
        _recordHook = SetWindowsHookEx(13, _recordCallback, GetModuleHandle(null), 0);
        if (_recordHook == IntPtr.Zero) return false;
        _recordWindow = window;
        _recorded = recorded;
        return true;
    }

    public void EndRecording()
    {
        if (_recordHook != IntPtr.Zero) UnhookWindowsHookEx(_recordHook);
        _recordHook = _recordWindow = IntPtr.Zero;
        _recorded = null;
        _recordKey = 0;
        _recordedShortcut = null;
    }

    private IntPtr OnRecordKey(int code, IntPtr message, IntPtr data)
    {
        var recorded = _recorded;
        if (code < 0 || recorded == null || NativeWindow.ForegroundHandle != _recordWindow || (Marshal.ReadInt32(data, 8) & 0x10) != 0)
            return CallNextHookEx(_recordHook, code, message, data);

        int key = Marshal.ReadInt32(data);
        int kind = message.ToInt32();
        if (_recordKey == 0 && (kind is 0x0100 or 0x0104) &&
            key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5))
        {
            var modifiers = CurrentModifiers();
            _recordKey = key;
            _recordedShortcut = key == 0x1B ? null : new QuickCaptureShortcut(modifiers, key);
            if (modifiers.HasFlag(ShortcutModifiers.Windows)) MaskWindowsMenu();
        }
        if (key != _recordKey) return CallNextHookEx(_recordHook, code, message, data);
        if (kind is 0x0101 or 0x0105)
        {
            var shortcut = _recordedShortcut;
            EndRecording();
            _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => recorded(shortcut)));
        }
        return new IntPtr(1);
    }

    private static ShortcutModifiers CurrentModifiers()
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        ShortcutModifiers modifiers = 0;
        if (Down(0x5B) || Down(0x5C)) modifiers |= ShortcutModifiers.Windows;
        if (Down(0x11)) modifiers |= ShortcutModifiers.Control;
        if (Down(0x12)) modifiers |= ShortcutModifiers.Alt;
        if (Down(0x10)) modifiers |= ShortcutModifiers.Shift;
        return modifiers;
    }

    private static void MaskWindowsMenu()
    {
        // The unassigned virtual key prevents a bare Win-key menu after consuming the chord.
        keybd_event(0xFF, 0, 0, UIntPtr.Zero);
        keybd_event(0xFF, 0, 2, UIntPtr.Zero);
    }
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && _active is { Hook: var hook } active && hook == IntPtr.Zero &&
            wParam.ToInt32() == active.Id)
        {
            int combination = lParam.ToInt32();
            if ((combination >> 16) == active.Shortcut.VirtualKey && (combination & 0xF) == (int)active.Shortcut.Modifiers)
            {
                handled = true;
                Queue(active);
            }
        }
        return IntPtr.Zero;
    }
    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        // No character capture or history: inspect only the requested W chord.
        var active = _active;
        var hook = active?.Hook ?? IntPtr.Zero;
        if (code >= 0 && active != null && hook != IntPtr.Zero && Marshal.ReadInt32(data) == 0x57 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
        {
            int kind = message.ToInt32();
            if ((kind is 0x0101 or 0x0105) && _held) { _held = false; return new IntPtr(1); }
            if (kind is 0x0100 or 0x0104)
            {
                if (_held) return new IntPtr(1);
                if (CurrentModifiers() == ShortcutModifiers.Windows)
                {
                    _held = true;
                    MaskWindowsMenu();
                    Queue(active); return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    private void Queue(Registration registration)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (ReferenceEquals(_active, registration)) _trigger(); }));
    }
    private void Release(Registration? registration)
    {
        if (registration == null) return;
        if (registration.Hook != IntPtr.Zero) UnhookWindowsHookEx(registration.Hook);
        else UnregisterHotKey(_source.Handle, registration.Id);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndRecording();
        Release(_active);
        _active = null;
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }
    private delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, KeyboardProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
