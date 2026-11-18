using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class QuickCaptureHotkey : IDisposable
{
    private const int Id = 0x4C56;
    private readonly Dispatcher _dispatcher;
    private readonly Action _trigger;
    private readonly HwndSource _source;
    private readonly KeyboardProc _callback;
    private IntPtr _hook;
    private bool _registered, _held, _disposed;
    public bool Enabled { get; private set; }

    public QuickCaptureHotkey(Dispatcher dispatcher, Action trigger)
    {
        _dispatcher = dispatcher; _trigger = trigger; _callback = OnKeyboard;
        _source = new HwndSource(new HwndSourceParameters("Levitation.QuickCapture") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
    }
    public void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == Enabled) return;
        Release();
        if (!enabled) return;
        // Prefer the normal hotkey API. Windows 11 may reserve Win+W for Widgets.
        _registered = RegisterHotKey(_source.Handle, Id, 0x0008 | 0x4000, 0x57);
        if (!_registered)
        {
            _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用 Win+W 快速记录，请在设置中关闭此选项后重新启用。");
        }
        Enabled = true;
    }
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == Id && Enabled) { handled = true; Queue(); }
        return IntPtr.Zero;
    }
    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        // No character capture or history: inspect only the requested W chord.
        if (code >= 0 && Enabled && Marshal.ReadInt32(data) == 0x57 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
        {
            int kind = message.ToInt32();
            if ((kind is 0x0101 or 0x0105) && _held) { _held = false; return new IntPtr(1); }
            if (kind is 0x0100 or 0x0104)
            {
                bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
                if (_held) return new IntPtr(1);
                if ((Down(0x5B) || Down(0x5C)) && !Down(0x11) && !Down(0x12) && !Down(0x10))
                {
                    _held = true;
                    // An unassigned virtual key masks the bare Win-key menu on release.
                    keybd_event(0xFF, 0, 0, UIntPtr.Zero); keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                    Queue(); return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    private void Queue()
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!_disposed && Enabled) _trigger(); }));
    }
    private void Release()
    {
        if (_registered) UnregisterHotKey(_source.Handle, Id);
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _registered = false; _hook = IntPtr.Zero; _held = false; Enabled = false;
    }
    public void Dispose() { if (_disposed) return; Release(); _disposed = true; _source.RemoveHook(OnMessage); _source.Dispose(); }
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
