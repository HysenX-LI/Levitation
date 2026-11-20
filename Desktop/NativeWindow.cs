using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Levitation.Desktop;

internal static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, in Input input, int size);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int maximum);
    private static IntPtr _contentWindow;
    public static IntPtr ForegroundHandle => GetForegroundWindow();
    public static Forms.Screen ForegroundScreen(IntPtr handle) => handle != IntPtr.Zero
        ? Forms.Screen.FromHandle(handle) : Forms.Screen.FromPoint(Forms.Cursor.Position);
    public static void ActivateFromShortcut(Window window)
    {
        if (window.Activate()) return;

        // A low-level hook can receive the shortcut while foreground permission stays with another process.
        // PowerToys Run uses an empty INPUT_MOUSE event to retry activation without moving or clicking.
        const uint inputMouse = 0;
        var input = new Input { Type = inputMouse };
        if (SendInput(1, in input, Marshal.SizeOf<Input>()) == 1) window.Activate();
    }
    public static void RestoreForeground(IntPtr handle) { if (handle != IntPtr.Zero && IsWindow(handle)) SetForegroundWindow(handle); }

    // Remember the working application before our floating window or the taskbar
    // takes focus. A tray-menu click should not select the taskbar's monitor.
    public static void RememberContentWindow()
    {
        var handle = GetForegroundWindow(); if (handle == IntPtr.Zero) return;
        GetWindowThreadProcessId(handle, out uint processId);
        if (processId == (uint)Environment.ProcessId) return;
        var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity);
        if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return;
        _contentWindow = handle;
    }
    public static Forms.Screen ContentScreen()
    {
        RememberContentWindow();
        return _contentWindow != IntPtr.Zero && IsWindow(_contentWindow)
            ? Forms.Screen.FromHandle(_contentWindow) : Forms.Screen.FromPoint(Forms.Cursor.Position);
    }
    public static void ShowCentered(Window window, Forms.Screen screen, double preferredWidth = 600, double preferredHeight = 690)
    {
        double scale = Scale(screen); var area = screen.WorkingArea;
        double availableWidth = Math.Max(1, area.Width / scale - 48), availableHeight = Math.Max(1, area.Height / scale - 48);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.MinWidth = Math.Min(430, availableWidth); window.MinHeight = Math.Min(380, availableHeight);
        window.Width = Math.Min(preferredWidth, availableWidth); window.Height = Math.Min(preferredHeight, availableHeight);
        window.WindowState = WindowState.Normal;
        bool opening = !window.IsVisible; double opacity = window.Opacity;
        if (opening) { window.Opacity = 0; window.Show(); }
        try
        {
            int width = (int)Math.Round(window.Width * scale), height = (int)Math.Round(window.Height * scale);
            // Keep the existing Z order; centering a utility window must not
            // turn it into an always-on-top window.
            SetWindowPos(new WindowInteropHelper(window).EnsureHandle(), IntPtr.Zero,
                area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height, 0x0004 | 0x0010);
        }
        finally { if (opening) window.Opacity = opacity; }
        window.Activate(); window.Focus();
    }

    public static void ThemeTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }

    public static void PlaceBounds(Window window, Forms.Screen screen, System.Windows.Rect bounds)
    {
        double scale = Scale(screen); var area = screen.WorkingArea;
        window.Width = bounds.Width; window.Height = bounds.Height;
        SetWindowPos(new WindowInteropHelper(window).EnsureHandle(), new IntPtr(-1),
            area.Left + (int)Math.Round(bounds.Left * scale), area.Top + (int)Math.Round(bounds.Top * scale),
            (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale), 0x0010);
    }

    public static Forms.Screen Screen(string? name) => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    public static double Scale(Forms.Screen screen)
    {
        var monitor = MonitorFromPoint(new Point { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 }, 2);
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1d;
    }
    public static double WorkHeight(Forms.Screen screen) => screen.WorkingArea.Height / Scale(screen);
    public static bool TryCursor(out int x, out int y) { bool found = GetCursorPos(out var point); x = point.X; y = point.Y; return found; }
    public static (double Left, double Top) RelativePosition(Window window, Forms.Screen screen)
    {
        double scale = Scale(screen); var area = screen.WorkingArea;
        if (GetWindowRect(new WindowInteropHelper(window).EnsureHandle(), out var rect)) return ((rect.Left - area.Left) / scale, (rect.Top - area.Top) / scale);
        return (0, 0);
    }
    public static void PlaceFloating(Window window, Forms.Screen screen, double x, double y)
    {
        double scale = Scale(screen); var area = screen.WorkingArea;
        int left = area.Left + (int)Math.Round(DockLayout.FloatingOffset(area.Width / scale, window.Width, x) * scale);
        int top = area.Top + (int)Math.Round(DockLayout.FloatingOffset(area.Height / scale, window.Height, y) * scale);
        SetWindowPos(new WindowInteropHelper(window).EnsureHandle(), new IntPtr(-1), left, top, (int)Math.Round(window.Width * scale), (int)Math.Round(window.Height * scale), 0x0010 | 0x0040);
    }
    public static void Place(Window window, Forms.Screen screen, bool edge = false, double dockPosition = .5, double edgeHeight = 148)
    {
        double scale = Scale(screen); var area = screen.WorkingArea;
        int width = (int)Math.Round(window.Width * scale), height = (int)Math.Round(window.Height * scale);
        int margin = edge ? 0 : (int)Math.Round(12 * scale);
        var layout = DockLayout.Calculate(area.Height / scale, window.Height, edgeHeight, dockPosition);
        int top = area.Top + (int)Math.Round((edge ? layout.EdgeTop : layout.PanelTop) * scale);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1), area.Right - width - margin, top, width, height, 0x0010 | 0x0040);
    }
    public static bool PointerInside(Window window)
    {
        if (!window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && GetCursorPos(out var point) && GetWindowRect(handle, out var rect) &&
            point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }
    public static bool PointerKeepsPanelOpen(Window window, Forms.Screen screen, bool docked)
    {
        if (!window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetCursorPos(out var pointer) || !GetWindowRect(handle, out var bounds)) return false;
        double scale = Scale(screen); var area = screen.WorkingArea;
        return DockLayout.PointerKeepsPanelOpen((pointer.X - area.Left) / scale, (pointer.Y - area.Top) / scale,
            (bounds.Left - area.Left) / scale, (bounds.Top - area.Top) / scale, (bounds.Right - bounds.Left) / scale, (bounds.Bottom - bounds.Top) / scale,
            area.Width / scale, area.Height / scale, docked);
    }
}
