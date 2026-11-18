using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Levitation.Desktop;

internal sealed class SettingsWindow : Window
{
    private readonly TaskService _service;
    private readonly DataStore _store;
    private readonly Action _repositionDock;
    private readonly StackPanel _trashTasks = new(), _trashCards = new(), _history = new();
    private readonly TabItem _trashTasksTab = new(), _trashCardsTab = new();
    private readonly ComboBox _year = new(), _month = new(), _day = new();
    private readonly TextBlock _historyCount = Ui.Text("", 12, true);
    private readonly TextBox _delay = Ui.Input("自动收起时间（秒）");
    private readonly CheckBox _autoStart = new(), _quickCapture = new();
    private readonly ComboBox _theme = new(), _monitor = new();
    private readonly Button _clearTrash;
    private bool _updating;

    public SettingsWindow(TaskService service, DataStore store, Action repositionDock)
    {
        Ui.ThemeWindow(this);
        _service = service; _store = store; _repositionDock = repositionDock;
        Title = "Levitation · 设置"; Width = 600; Height = 690;
        MinWidth = 430; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.Manual; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Levitation;component/Desktop/SettingsStyles.xaml", UriKind.Relative)
        });

        var tabs = SettingsLayout.TabStrip();
        tabs.TabStripPlacement = Dock.Left;
        SizeChanged += (_, e) => tabs.TabStripPlacement = e.NewSize.Width >= 540 ? Dock.Left : Dock.Top;
        Content = tabs;

        _clearTrash = SettingsLayout.Action("清空垃圾堆", ClearTrash, danger: true);
        tabs.Items.Add(SettingsLayout.Tab("浮窗行为", GeneralPage()));
        tabs.Items.Add(SettingsLayout.Tab("垃圾堆", TrashPage()));
        tabs.Items.Add(SettingsLayout.Tab("完成历史", HistoryPage()));
        tabs.Items.Add(SettingsLayout.Tab("数据与备份", DataPage()));
        tabs.Items.Add(SettingsLayout.Tab("关于", AboutPage()));

        ReloadGeneral();
        FillHistoryFilters(true);
        _year.SelectionChanged += (_, _) => { if (_updating) return; _month.SelectedIndex = 0; _day.SelectedIndex = 0; FillDays(); RefreshHistory(); };
        _month.SelectionChanged += (_, _) => { if (_updating) return; _day.SelectedIndex = 0; FillDays(); RefreshHistory(); };
        _day.SelectionChanged += (_, _) => { if (!_updating) RefreshHistory(); };
        RefreshData();
    }

    private UIElement GeneralPage()
    {
        foreach (var text in new[] { "跟随系统", "浅色", "深色" }) _theme.Items.Add(text);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            const string displayPrefix = @"\\.\DISPLAY";
            string label = screen.DeviceName.StartsWith(displayPrefix, StringComparison.OrdinalIgnoreCase)
                ? $"显示器 {screen.DeviceName[displayPrefix.Length..]}" : screen.DeviceName;
            _monitor.Items.Add(new ComboBoxItem
            {
                Content = label + (screen.Primary ? "（主屏）" : ""),
                ToolTip = screen.DeviceName,
                Tag = screen.DeviceName
            });
        }
        SettingsLayout.Field(_theme, 138);
        SettingsLayout.Field(_monitor, 138);
        SettingsLayout.Field(_delay, 67);

        var unit = Ui.Text("秒", 11, true);
        unit.Margin = new Thickness(7, 0, 0, 0);
        var delayField = new StackPanel { Orientation = Orientation.Horizontal };
        delayField.Children.Add(_delay);
        delayField.Children.Add(unit);

        var appearance = SettingsLayout.Section("显示与停靠",
            SettingsLayout.Row("主题", null, _theme),
            SettingsLayout.Row("停靠显示器", null, _monitor),
            SettingsLayout.Row("自动收起", "鼠标移开后等待 0.2～30 秒。默认 0.5 秒。", delayField),
            SettingsLayout.Row("窗口大小", "恢复浮窗和两张内容卡片的默认尺寸。",
                SettingsLayout.Action("恢复默认", () =>
                {
                    _service.ResetWindowSize();
                    _repositionDock();
                })));
        var startup = SettingsLayout.Section("启动与快捷键",
            SettingsLayout.Row("开机启动", "登录 Windows 时启动 Levitation。", _autoStart),
            SettingsLayout.Row("快速记录待办", "使用 Win+W，会暂时占用 Windows 小组件快捷键。", _quickCapture));

        var save = SettingsLayout.Action("保存设置", SaveGeneral, primary: true);
        return SettingsLayout.Page("浮窗行为", null, Ui.Scroll(Ui.Stack(appearance, startup)), save);
    }

    private UIElement TrashPage()
    {
        var tabs = SettingsLayout.TabStrip();
        tabs.Tag = _clearTrash;
        _trashTasks.Margin = _trashCards.Margin = new Thickness(0, 9, 0, 0);
        _trashTasksTab.Header = "待办";
        _trashTasksTab.Content = Ui.Scroll(_trashTasks);
        _trashCardsTab.Header = "常用内容";
        _trashCardsTab.Content = Ui.Scroll(_trashCards);
        tabs.Items.Add(_trashTasksTab);
        tabs.Items.Add(_trashCardsTab);
        return SettingsLayout.Page("垃圾堆", "保留最近删除的 10 项，可从这里恢复。", tabs);
    }

    private UIElement HistoryPage()
    {
        var body = new DockPanel();
        var filters = new UniformGrid { Columns = 3 };
        foreach (var (label, combo) in new[] { ("年份", _year), ("月份", _month), ("日期", _day) })
        {
            SettingsLayout.Field(combo);
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            var field = Ui.Field(label, combo);
            field.Margin = filters.Children.Count < 2 ? new Thickness(0, 0, 8, 0) : new Thickness(0);
            filters.Children.Add(field);
        }
        var filterSurface = SettingsLayout.Surface(filters);
        filterSurface.Padding = new Thickness(10, 8, 10, 10);
        DockPanel.SetDock(filterSurface, Dock.Top);
        body.Children.Add(filterSurface);
        _historyCount.Margin = new Thickness(0, 11, 0, 7);
        DockPanel.SetDock(_historyCount, Dock.Top);
        body.Children.Add(_historyCount);
        body.Children.Add(Ui.Scroll(_history));
        return SettingsLayout.Page("完成历史", null, body);
    }

    private UIElement AboutPage()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        var version = Ui.Text(assembly.GetName().Version?.ToString(3) ?? "", 12, true);
        var app = SettingsLayout.Section("应用",
            SettingsLayout.Row("Levitation", "Windows 侧边待办与常用内容浮窗。", version),
            SettingsLayout.Row("项目源码", "在 GitHub 查看源码和发布记录。",
                SettingsLayout.Action("打开 GitHub", () => Process.Start(new ProcessStartInfo("https://github.com/HysenX-LI/Levitation") { UseShellExecute = true }))));
        var credits = SettingsLayout.Section("开源与许可",
            SettingsLayout.Row("GNU AGPL-3.0", "按许可证重新分发。本软件按现状提供，无担保。",
                SettingsLayout.Action("查看许可证", ViewLicense)),
            SettingsLayout.Row("WinKit 2.11", "Levitation 基于 li5bo5/WinKit 2.11 重构。\nWinKit © 2026 li5bo5. All Rights Reserved."));
        return SettingsLayout.Page("关于", null, Ui.Scroll(Ui.Stack(app, credits)));
    }

    private void ViewLicense()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        using var stream = assembly.GetManifestResourceStream("Levitation.LICENSE")
            ?? throw new InvalidOperationException("未找到许可证正文。");
        using var reader = new StreamReader(stream);
        var text = Ui.Input("AGPL-3.0 许可证正文", multiline: true);
        text.MaxLength = 0;
        text.Text = reader.ReadToEnd();
        text.IsReadOnly = true;
        text.MaxHeight = double.PositiveInfinity;
        text.Margin = new Thickness(12);
        var dialog = Ui.Dialog("GNU AGPL-3.0", this, 540);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Height = Math.Min(480, dialog.MaxHeight);
        dialog.Content = text;
        dialog.InputBindings.Add(new KeyBinding(ApplicationCommands.Close, Key.Escape, ModifierKeys.None));
        dialog.CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => dialog.Close()));
        dialog.ShowDialog();
    }

    private void SaveGeneral()
    {
        if (!double.TryParse(_delay.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) || !double.IsFinite(seconds) || seconds < .2 || seconds > 30)
            throw new ArgumentException("自动收起时间请输入 0.2～30 秒。");
        var preferences = new Preferences
        {
            AutoHideSeconds = seconds, Theme = _theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" },
            QuickCaptureEnabled = _quickCapture.IsChecked == true,
            MonitorName = (_monitor.SelectedItem as ComboBoxItem)?.Tag as string, Width = _service.State.Preferences.Width, Height = _service.State.Preferences.Height,
            TasksExpanded = _service.State.Preferences.TasksExpanded, CardsExpanded = _service.State.Preferences.CardsExpanded,
            TasksHeight = _service.State.Preferences.TasksHeight, CardsHeight = _service.State.Preferences.CardsHeight,
            DockPosition = _service.State.Preferences.DockPosition,
            IsDocked = _service.State.Preferences.IsDocked, FloatingX = _service.State.Preferences.FloatingX, FloatingY = _service.State.Preferences.FloatingY,
            Extra = _service.State.Preferences.Extra
        };
        _service.SavePreferences(preferences);
        AutoStart.Set(_autoStart.IsChecked == true);
        Ui.ApplyTheme(preferences.Theme);
        _repositionDock();
    }

    private UIElement DataPage()
    {
        var path = Ui.Input("数据保存目录", _store.DirectoryPath);
        path.IsReadOnly = true;
        path.ToolTip = _store.DirectoryPath;
        path.Margin = new Thickness(12, 0, 12, 10);
        SettingsLayout.Field(path);
        var location = SettingsLayout.Section("存储位置",
            Ui.Stack(SettingsLayout.Row("数据目录", "待办、历史和常用内容保存在 Windows 用户目录。",
                SettingsLayout.Action("打开文件夹", () => Process.Start(new ProcessStartInfo(_store.DirectoryPath) { UseShellExecute = true }))), path));
        var backup = SettingsLayout.Section("备份与恢复",
            SettingsLayout.Row("完整备份", "将待办、常用内容、截图和设置导出为一个文件。", SettingsLayout.Action("导出…", ExportBackup)),
            SettingsLayout.Row("恢复备份", "恢复前会自动备份当前数据。", SettingsLayout.Action("选择备份…", RestoreBackup)),
            SettingsLayout.Row("自动快照", "保留上一个有效版本和最近 14 份每日快照。"));
        var migration = SettingsLayout.Section("迁移",
            SettingsLayout.Row("导入 WinKit 数据", "选择旧版 Data 文件夹导入，原文件会保留。", SettingsLayout.Action("选择文件夹…", ImportLegacy)));
        return SettingsLayout.Page("数据与备份", null, Ui.Scroll(Ui.Stack(location, backup, migration)));
    }

    private void ExportBackup()
    {
        var picker = new SaveFileDialog { Filter = "Levitation 备份 (*.json)|*.json",
            FileName = $"Levitation-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json", AddExtension = true };
        if (picker.ShowDialog(this) == true) _store.Export(picker.FileName);
    }

    private void RestoreBackup()
    {
        var picker = new OpenFileDialog { Filter = "Levitation 备份 (*.json)|*.json" };
        if (picker.ShowDialog(this) != true) return;
        var preview = DataStore.ReadFile(picker.FileName);
        if (MessageBox.Show(this, $"备份包含 {preview.Tasks.Count} 条待办、{preview.Cards.Count} 张卡片和 {preview.History.Count} 条完成记录。\n\n恢复会替换当前数据；操作前会自动另存当前数据，可用同一入口还原。", "恢复完整备份", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _service.RestoreBackup(picker.FileName);
        RefreshData();
        ReloadGeneral();
        Ui.ApplyTheme(_service.State.Preferences.Theme);
        _repositionDock();
    }

    private void ImportLegacy()
    {
        var picker = new OpenFolderDialog { Title = "选择旧版 WinKit 的 Data 文件夹", Multiselect = false };
        if (picker.ShowDialog(this) == true)
            MessageBox.Show(this, $"已导入 {_service.ImportLegacy(picker.FolderName)} 条记录；相同来源不会重复导入。", "Levitation");
    }

    private void ReloadGeneral()
    {
        _delay.Text = _service.State.Preferences.AutoHideSeconds.ToString(CultureInfo.CurrentCulture);
        _theme.SelectedIndex = _service.State.Preferences.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _monitor.SelectedItem = _monitor.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _service.State.Preferences.MonitorName)
            ?? _monitor.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == NativeWindow.Screen(null).DeviceName);
        _autoStart.IsChecked = AutoStart.Enabled;
        _quickCapture.IsChecked = _service.State.Preferences.QuickCaptureEnabled;
    }

    public void RefreshData()
    {
        _trashTasks.Children.Clear(); _trashCards.Children.Clear();
        _clearTrash.IsEnabled = _service.State.Trash.Count > 0;
        foreach (bool tasks in new[] { true, false })
        {
            var entries = _service.State.Trash.Where(t => tasks ? t.Task != null : t.Card != null).ToList();
            var list = tasks ? _trashTasks : _trashCards;
            (tasks ? _trashTasksTab : _trashCardsTab).Header = $"{(tasks ? "待办" : "常用内容")}  {entries.Count}";
            if (entries.Count == 0)
            {
                list.Children.Add(SettingsLayout.Empty(tasks ? "暂无删除的待办" : "暂无删除的常用内容"));
                continue;
            }
            var rows = new List<UIElement>();
            foreach (var entry in entries)
            {
                var restore = SettingsLayout.Action("恢复", () => _service.RestoreTrash(entry.Id));
                System.Windows.Automation.AutomationProperties.SetName(restore, $"恢复：{entry.Title}");
                if (entry.ParentTaskId.HasValue)
                {
                    var parent = TaskTree.Find(_service.State.Tasks, entry.ParentTaskId.Value);
                    restore.ToolTip = parent != null && parent.Depth + 1 + TaskTree.Height(entry.Task!) <= TaskTree.MaximumDepth
                        ? "恢复到原待办下" : "原位置不存在或层级已满，恢复为普通待办";
                }
                var detail = entry.ParentTaskId.HasValue
                    ? $"子待办 · {entry.ParentTitle}   {entry.DeletedAt:yyyy-M-d HH:mm}"
                    : entry.DeletedAt.ToString("yyyy-M-d HH:mm", CultureInfo.InvariantCulture);
                rows.Add(SettingsLayout.Row(entry.Title, detail, restore, singleLineTitle: true));
            }
            list.Children.Add(SettingsLayout.Surface(SettingsLayout.SeparatedRows(rows)));
        }
        FillHistoryFilters(false); RefreshHistory();
    }

    private void ClearTrash()
    {
        int count = _service.State.Trash.Count;
        if (count == 0) return;
        if (MessageBox.Show(this, $"清空垃圾堆中的 {count} 条记录？\n清空后无法在垃圾堆中恢复。", "清空垃圾堆", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            _service.ClearTrash();
    }

    private static int Selected(ComboBox combo) => combo.SelectedItem is ComboBoxItem { Tag: int value } ? value : 0;
    private static ComboBoxItem Choice(string text, int value) => new() { Content = text, Tag = value };
    private void FillHistoryFilters(bool initial)
    {
        _updating = true;
        int oldYear = initial ? _service.Today.Year : Selected(_year), oldMonth = initial ? _service.Today.Month : Selected(_month);
        _year.Items.Clear(); _year.Items.Add(Choice("全部年份", 0));
        foreach (int year in _service.State.History.Select(h => h.Day.Year).Append(_service.Today.Year).Distinct().OrderDescending()) _year.Items.Add(Choice(year + "年", year));
        _year.SelectedItem = _year.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == oldYear) ?? _year.Items[0];
        _month.Items.Clear(); _month.Items.Add(Choice("全部月份", 0)); for (int i = 1; i <= 12; i++) _month.Items.Add(Choice(i + "月", i)); _month.SelectedIndex = oldMonth;
        FillDays(); _updating = false;
    }
    private void FillDays()
    {
        bool before = _updating; _updating = true; int previous = Selected(_day);
        int year = Selected(_year), month = Selected(_month), count = year > 0 && month > 0 ? DateTime.DaysInMonth(year, month) : 31;
        _day.Items.Clear(); _day.Items.Add(Choice("全部日期", 0)); for (int i = 1; i <= count; i++) _day.Items.Add(Choice(i + "日", i));
        _day.SelectedIndex = Math.Min(previous, count); _updating = before;
    }
    private void RefreshHistory()
    {
        int year = Selected(_year), month = Selected(_month), day = Selected(_day);
        var records = _service.State.History.Where(h => (year == 0 || h.Day.Year == year) && (month == 0 || h.Day.Month == month) && (day == 0 || h.Day.Day == day)).OrderByDescending(h => h.CompletedAt).ToList();
        _historyCount.Text = $"共 {records.Count} 条已完成待办"; _history.Children.Clear();
        if (records.Count == 0) _history.Children.Add(SettingsLayout.Empty("这段时间还没有完成记录。"));
        var activeCompletions = _service.State.Tasks.Where(task => task.CompletedAt.HasValue && task.CompletionId.HasValue)
            .ToLookup(task => task.CompletionId!.Value);
        foreach (var group in records.GroupBy(h => h.Day))
        {
            var rows = new List<UIElement>();
            foreach (var record in group)
            {
                var entry = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                var item = Ui.Text("✓  " + record.Title, 12);
                item.FontWeight = FontWeights.Medium;
                item.Margin = new Thickness(12, 4, 12, 4);
                item.ToolTip = record.CompletedAt.ToString("yyyy-M-d HH:mm:ss", CultureInfo.InvariantCulture);
                entry.Children.Add(item);
                var subtasks = record.Subtasks;
                if (subtasks == null && activeCompletions[record.Id].FirstOrDefault() is { } task)
                    subtasks = TaskTree.CaptureSubtasks(task);
                if (subtasks != null)
                    foreach (var subtask in subtasks) entry.Children.Add(CreateHistorySubtaskRow(subtask));
                rows.Add(entry);
            }
            _history.Children.Add(SettingsLayout.Section($"{group.Key:yyyy年M月d日}   ·   {rows.Count} 项", rows.ToArray()));
        }
    }

    private static Grid CreateHistorySubtaskRow(CompletionSubtask subtask)
    {
        var row = new Grid { MinHeight = 24, Margin = new Thickness(25 + (subtask.Depth - 1) * 14, 0, 12, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var status = Ui.Text(subtask.IsCompleted ? "✓" : "○", 11, muted: true);
        var title = Ui.Text(subtask.Title, 12, muted: !subtask.IsCompleted);
        title.Margin = new Thickness(0, 3, 0, 3);
        Grid.SetColumn(title, 1);
        row.Children.Add(status);
        row.Children.Add(title);

        if (subtask.IsCompleted && subtask.CompletedAt is { } completedAt)
        {
            var date = Ui.Text(completedAt.ToString("yyyy-M-d", CultureInfo.InvariantCulture), 11, muted: true);
            date.Margin = new Thickness(16, 0, 0, 0);
            date.TextWrapping = TextWrapping.NoWrap;
            date.ToolTip = completedAt.ToString("yyyy-M-d HH:mm:ss", CultureInfo.InvariantCulture);
            Grid.SetColumn(date, 2);
            row.Children.Add(date);
        }
        return row;
    }
}
