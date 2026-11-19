using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Levitation.Desktop;

public sealed class App : System.Windows.Application
{
    private Mutex? _mutex;
    private DataStore? _store;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private TaskService? _service;
    private DockWindow? _dock;
    private QuickCaptureHotkey? _quickHotkey;
    private QuickCaptureWindow? _quickCapture;
    private (bool Enabled, QuickCaptureShortcut? Shortcut)? _quickRequested;
    internal bool Exiting { get; private set; }
    public static new App Current => (App)System.Windows.Application.Current;
    internal QuickCaptureHotkey QuickHotkey => _quickHotkey ?? throw new InvalidOperationException("快速记录尚未初始化。");

    [STAThread]
    public static void Main()
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        new App().Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) => { Report(args.Exception); args.Handled = true; };
        // Keep the legacy mutex so renamed and older builds cannot run together.
        _mutex = new Mutex(true, "WinKit_SingleInstance_Mutex_li5bo5", out bool created);
        if (!created) { MessageBox.Show("Levitation 已在运行，请从系统托盘打开。", "Levitation"); Shutdown(); return; }
        try
        {
            string legacy = Path.Combine(AppContext.BaseDirectory, "Data");
            _store = new DataStore(legacyDirectory: Directory.Exists(legacy) ? legacy : null);
            _service = new TaskService(_store);
            Ui.ApplyTheme(_service.State.Preferences.Theme); Ui.InstallStyles();
            AutoStart.SyncPath();
            _service.EnsureToday();
            _dock = new DockWindow(_service, _store);
            MainWindow = _dock;
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Levitation.PTD.ico");
            _trayIcon = iconStream == null ? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone() : new System.Drawing.Icon(iconStream);
            var menu = new TrayMenu();
            menu.AddAction(TrayAction.Expand, "展开待办", () => Dispatcher.Invoke(() => _dock.Expand()));
            menu.AddAction(TrayAction.Routines, "日常任务", () => Dispatcher.Invoke(() => _dock.OpenRoutines()));
            menu.AddAction(TrayAction.Settings, "设置", () => Dispatcher.Invoke(() => _dock.OpenSettings()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.AddAction(TrayAction.Exit, "退出", () => Dispatcher.Invoke(ExitApp));
            _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "Levitation · 待办与常用内容", Visible = true, ContextMenuStrip = menu };
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => _dock.Expand());
            _quickHotkey = new QuickCaptureHotkey(Dispatcher, OpenQuickCapture);
            _service.Changed += SyncQuickCapture;
            SyncQuickCapture();
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.UserPreferenceChanged += PreferenceChanged;
            _dock.StartDocked();
            if (_store.RecoverySource != null)
                MessageBox.Show($"主数据未能读取，已加载有效备份：\n{_store.RecoverySource}\n原始文件会保留，后续修改将继续安全保存。", "Levitation 数据恢复");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动未完成，原数据不会被空文件覆盖。\n\n{ex.Message}\n\n数据目录：{DataStore.DefaultDirectory}", "Levitation", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        base.OnStartup(e);
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => _dock?.Reposition());
    private void OpenQuickCapture()
    {
        if (_service == null || Exiting) return;
        _dock?.CancelTaskReorder();
        (_quickCapture ??= new QuickCaptureWindow(_service)).Toggle();
    }
    private void SyncQuickCapture()
    {
        if (_service == null || _quickHotkey == null) return;
        var preferences = _service.State.Preferences;
        var requested = (preferences.QuickCaptureEnabled, preferences.QuickCaptureShortcut);
        if (_quickRequested == requested) return;
        _quickRequested = requested;
        var error = ApplyQuickCapture(preferences);
        if (error != null) _tray?.ShowBalloonTip(6000, "快速记录快捷键未更新", error, Forms.ToolTipIcon.Warning);
    }

    private string? ApplyQuickCapture(Preferences preferences, Action<Action>? save = null)
    {
        var custom = preferences.QuickCaptureShortcut;
        return QuickHotkey.Apply(preferences.QuickCaptureEnabled ? custom ?? QuickCaptureShortcut.Default : null,
            allowWidgetsFallback: custom == null, save);
    }

    internal string? SaveGeneralPreferences(Preferences preferences) =>
        ApplyQuickCapture(preferences, activate => _service!.SavePreferences(preferences, () =>
        {
            activate();
            _quickRequested = (preferences.QuickCaptureEnabled, preferences.QuickCaptureShortcut);
        }));
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    { if (_service != null) Ui.ApplyTheme(_service.State.Preferences.Theme); });
    public void ExitApp() { Exiting = true; Shutdown(); }
    public static void Try(Action action) { try { action(); } catch (Exception ex) { Report(ex); } }
    private static void Report(Exception ex)
    {
        try { Directory.CreateDirectory(DataStore.DefaultDirectory); File.AppendAllText(Path.Combine(DataStore.DefaultDirectory, "crash.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch { }
        MessageBox.Show($"操作未能完成，请查看以下原因。数据写入失败时会保留上一个有效版本。\n\n{ex.Message}", "Levitation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Exiting = true;
        if (_service != null) _service.Changed -= SyncQuickCapture;
        _quickHotkey?.Dispose(); _quickCapture?.Close();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        _dock?.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose(); _store?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose(); base.OnExit(e);
    }
}

internal static class AutoStart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Entry = "Levitation", LegacyEntry = "WinKit";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return (key?.GetValue(Entry) is string value && !string.IsNullOrWhiteSpace(value)) ||
                (key?.GetValue(LegacyEntry) is string legacy && !string.IsNullOrWhiteSpace(legacy));
        }
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue(Entry, $"\"{Environment.ProcessPath}\""); else key.DeleteValue(Entry, false);
        key.DeleteValue(LegacyEntry, false);
    }
    public static void SyncPath() { if (Enabled) Set(true); }
}
