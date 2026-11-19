using System.Windows.Automation;
using System.Windows.Interop;

namespace Levitation.Desktop;

internal sealed class HotkeyEditor : StackPanel
{
    private readonly TextBlock _status = Ui.Text("", 11, true);
    private readonly TextBlock _label = Ui.Text("", 12);
    private readonly Button _record;
    private object? _session;
    private Window? _recordingWindow;
    public QuickCaptureShortcut? Shortcut { get; private set; }

    public HotkeyEditor()
    {
        Margin = new Thickness(12, 8, 12, 10);
        Children.Add(Ui.Text("快速记录快捷键", 12, true));
        _record = SettingsLayout.Action("录制", Record);
        AutomationProperties.SetName(_record, "录制快速记录快捷键");
        _record.LostKeyboardFocus += (_, _) => CancelRecording();
        Unloaded += (_, _) => CancelRecording();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelRecording(); };

        var controls = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        _label.Margin = new Thickness(0, 0, 10, 4);
        _record.Margin = new Thickness(0, 0, 6, 4);
        var reset = SettingsLayout.Action("恢复默认", () => LoadShortcut(null));
        AutomationProperties.SetName(reset, "恢复默认快捷键 Win+W");
        reset.Margin = new Thickness(0, 0, 0, 4);
        controls.Children.Add(_label);
        controls.Children.Add(_record);
        controls.Children.Add(reset);
        Children.Add(controls);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Children.Add(_status);
        LoadShortcut(null);
    }

    public void LoadShortcut(QuickCaptureShortcut? shortcut)
    {
        CancelRecording();
        Shortcut = shortcut;
        _label.Text = Format(shortcut ?? QuickCaptureShortcut.Default);
        Status(shortcut == null ? "Win+W 会暂时占用 Windows 小组件快捷键。" : "");
    }

    private void Record()
    {
        if (_session != null) { CancelRecording(); return; }
        if (Window.GetWindow(this) is not { IsActive: true } window || !_record.Focus()) return;
        var session = new object();
        if (!App.Current.QuickHotkey.BeginRecording(new WindowInteropHelper(window).Handle, shortcut =>
        {
            if (!ReferenceEquals(session, _session)) return;
            CancelRecording();
            if (shortcut is { IsValid: true }) LoadShortcut(shortcut);
            else if (shortcut.HasValue) ShowError("请同时按一个修饰键（Ctrl、Alt、Shift 或 Win）和一个主键。");
        }))
        {
            ShowError("暂时无法开始录制，请重试。");
            return;
        }
        _session = session;
        _recordingWindow = window;
        _record.Content = "取消录制";
        window.Deactivated += RecordingWindowDeactivated;
        Status("按下组合键，或按 Esc 取消。");
    }

    private void RecordingWindowDeactivated(object? sender, EventArgs e) => CancelRecording();

    public void CancelRecording()
    {
        if (_session == null) return;
        _session = null;
        if (_recordingWindow is { } window) window.Deactivated -= RecordingWindowDeactivated;
        _recordingWindow = null;
        App.Current.QuickHotkey.EndRecording();
        _record.Content = "录制";
        Status(Shortcut == null ? "Win+W 会暂时占用 Windows 小组件快捷键。" : "");
    }

    public void ShowError(string text)
    {
        Status(text, error: true);
        _status.BringIntoView();
    }

    private void Status(string message, bool error = false)
    {
        _status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
        _status.Text = message;
        _status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Format(QuickCaptureShortcut shortcut)
    {
        if (!shortcut.IsValid) return "无效快捷键";
        var keys = new List<string>(5);
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control)) keys.Add("Ctrl");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt)) keys.Add("Alt");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift)) keys.Add("Shift");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Windows)) keys.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(shortcut.VirtualKey);
        keys.Add(key == Key.Space ? "空格" : new KeyConverter().ConvertToInvariantString(key) ?? key.ToString());
        return string.Join("+", keys);
    }
}
