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
    private readonly TextBox _delay;
    private readonly CheckBox _autoStart, _quickCapture;
    private readonly ComboBox _theme = new(), _monitor = new();
    private readonly Button _clearTrash;
    private bool _updating;

    public SettingsWindow(TaskService service, DataStore store, Action repositionDock)
    {
        Ui.ThemeWindow(this);
        _service = service; _store = store; _repositionDock = repositionDock;
        Title = "Levitation · 设置"; Width = 600; Height = 690;
        MinWidth = 430; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.Manual; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var tabs = new TabControl { Margin = new Thickness(16), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        Content = tabs;

        var general = new StackPanel { Margin = new Thickness(12) };
        _delay = Ui.Input("自动收起等待时间", service.State.Preferences.AutoHideSeconds.ToString(CultureInfo.CurrentCulture));
        _delay.Width = 96;
        var delayField = new StackPanel { Orientation = Orientation.Horizontal }; delayField.Children.Add(_delay);
        var unit = Ui.Text("秒", 12, true); unit.Margin = new Thickness(10, 0, 0, 0); delayField.Children.Add(unit);
        general.Children.Add(Ui.Field("自动收起等待时间（0.2～30 秒）", delayField));
        var hint = Ui.Text("默认离开 0.5 秒后收起。打字、拖动或固定展开时暂停，未提交的草稿会保留。", 12, true); hint.Margin = new Thickness(0, 8, 0, 9); general.Children.Add(hint);
        _theme.Items.Add("跟随系统"); _theme.Items.Add("浅色"); _theme.Items.Add("深色");
        _theme.SelectedIndex = service.State.Preferences.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 }; _theme.Height = _theme.MinHeight = 34;
        Ui.Label(general, "外观", _theme);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            _monitor.Items.Add(new ComboBoxItem { Content = screen.DeviceName + (screen.Primary ? "（主显示器）" : ""), Tag = screen.DeviceName });
        var selected = _monitor.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == service.State.Preferences.MonitorName);
        _monitor.SelectedItem = selected ?? _monitor.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == NativeWindow.Screen(null).DeviceName);
        _monitor.Height = _monitor.MinHeight = 34; Ui.Label(general, "停靠的显示器", _monitor);
        _autoStart = new CheckBox { Content = "开机时自动启动 Levitation", IsChecked = AutoStart.Enabled, Margin = new Thickness(0, 20, 0, 12) }; general.Children.Add(_autoStart);
        _quickCapture = new CheckBox { Content = "启用 Win+W 快速记录待办", IsChecked = service.State.Preferences.QuickCaptureEnabled, Margin = new Thickness(0, 0, 0, 6) }; general.Children.Add(_quickCapture);
        general.Children.Add(Ui.Text("软件运行且此项启用时，Win+W 用于快速记录；Windows 小组件的同名快捷键会暂时被占用。", 11, true));
        general.Children.Add(Ui.Buttons(Ui.Action("恢复默认窗口大小", () =>
        {
            _service.ResetWindowSize(); _repositionDock();
        }), Ui.Action("保存设置", SaveGeneral, true)));
        var sizeHint = Ui.Text("恢复主浮窗及两张卡片的默认尺寸，保留内容与其他设置。", 11, true); sizeHint.Margin = new Thickness(0, 8, 0, 0); general.Children.Add(sizeHint);
        tabs.Items.Add(Tab("浮窗行为", Ui.Scroll(general)));

        var trashBody = new Grid { Margin = new Thickness(12) };
        trashBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); trashBody.RowDefinitions.Add(new RowDefinition());
        _clearTrash = Ui.Action("清空垃圾堆", ClearTrash); _clearTrash.SetResourceReference(Control.ForegroundProperty, "Danger");
        var trashActions = Ui.Buttons(_clearTrash); trashActions.Margin = new Thickness(0); Panel.SetZIndex(trashActions, 1);
        var trashTabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        _trashTasks.Margin = _trashCards.Margin = new Thickness(0, 12, 0, 0);
        _trashTasksTab.Header = "待办"; _trashTasksTab.Content = Ui.Scroll(_trashTasks); _trashTasksTab.Padding = new Thickness(16, 7, 16, 7);
        _trashCardsTab.Header = "常用内容"; _trashCardsTab.Content = Ui.Scroll(_trashCards); _trashCardsTab.Padding = new Thickness(16, 7, 16, 7);
        trashTabs.Items.Add(_trashTasksTab); trashTabs.Items.Add(_trashCardsTab); Grid.SetRowSpan(trashTabs, 2); trashBody.Children.Add(trashTabs); trashBody.Children.Add(trashActions);
        tabs.Items.Add(Tab("垃圾堆", trashBody));

        var historyRoot = new Grid { Margin = new Thickness(12) };
        historyRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); historyRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); historyRoot.RowDefinitions.Add(new RowDefinition());
        var filters = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 12) };
        foreach (var (label, combo) in new[] { ("年份", _year), ("月份", _month), ("日期", _day) })
        {
            combo.Height = combo.MinHeight = 34; var field = Ui.Field(label, combo); field.Margin = new Thickness(0, 0, 8, 0); filters.Children.Add(field);
        }
        historyRoot.Children.Add(filters); _historyCount.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(_historyCount, 1); historyRoot.Children.Add(_historyCount);
        var historyScroll = Ui.Scroll(_history); Grid.SetRow(historyScroll, 2); historyRoot.Children.Add(historyScroll);
        tabs.Items.Add(Tab("完成历史", historyRoot));
        tabs.Items.Add(Tab("数据与备份", DataPanel()));
        tabs.Items.Add(Tab("关于", AboutPanel()));

        FillHistoryFilters(true);
        _year.SelectionChanged += (_, _) => { if (_updating) return; _month.SelectedIndex = 0; _day.SelectedIndex = 0; FillDays(); RefreshHistory(); };
        _month.SelectionChanged += (_, _) => { if (_updating) return; _day.SelectedIndex = 0; FillDays(); RefreshHistory(); };
        _day.SelectionChanged += (_, _) => { if (!_updating) RefreshHistory(); };
        RefreshData();
    }

    private static TabItem Tab(string title, UIElement content) => new() { Header = title, Content = content, Padding = new Thickness(9, 7, 9, 7) };

    private UIElement AboutPanel()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        var title = Ui.Text($"Levitation {assembly.GetName().Version?.ToString(3)}", 18);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 12);
        var notices = Ui.Text("基于 li5bo5/WinKit 2.11 重构。\nWinKit © 2026 li5bo5. All Rights Reserved.\n本软件按 GNU AGPL-3.0 发布，你可按该许可证重新分发。本软件按现状提供，无担保。");
        var actions = Ui.Buttons(
            Ui.Action("项目源码", () => Process.Start(new ProcessStartInfo("https://github.com/HysenX-LI/Levitation") { UseShellExecute = true })),
            Ui.Action("查看许可证", () =>
            {
                using var stream = assembly.GetManifestResourceStream("Levitation.LICENSE") ?? throw new InvalidOperationException("未找到许可证正文。");
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
            }));
        var panel = Ui.Stack(title, notices, actions);
        panel.Margin = new Thickness(12);
        return Ui.Scroll(panel);
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

    private UIElement DataPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        var heading = Ui.Text("数据独立于程序保存", 15); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 9, 0, 12); panel.Children.Add(heading);
        panel.Children.Add(Ui.Text("更新时替换程序文件即可；原有待办、完成历史、日常安排和卡片会继续读取。数据不放在版本号目录或安装目录中。", 12, true));
        var path = Ui.Input("数据保存目录", _store.DirectoryPath); path.IsReadOnly = true; path.Margin = new Thickness(0, 12, 0, 8); panel.Children.Add(path);
        var backupHint = Ui.Text("自动保留上一个有效版本及最近 14 份每日快照。换电脑前，可导出包含全部数据的备份文件。", 12, true); backupHint.Margin = new Thickness(0, 20, 0, 10); panel.Children.Add(backupHint);
        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 10), MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Stretch };
        void AddAction(string label, Action action)
        {
            var button = Ui.Action(label, action); button.Margin = new Thickness(5); button.MinHeight = 64; button.FontSize = 12;
            button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button);
        }
        AddAction("打开数据文件夹", () => Process.Start(new ProcessStartInfo(_store.DirectoryPath) { UseShellExecute = true }));
        AddAction("导出完整数据备份", () =>
        {
            var picker = new SaveFileDialog { Filter = "Levitation 备份 (*.json)|*.json", FileName = $"Levitation-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json", AddExtension = true };
            if (picker.ShowDialog(this) == true) _store.Export(picker.FileName);
        });
        AddAction("从备份文件恢复数据", () =>
        {
            var picker = new OpenFileDialog { Filter = "Levitation 备份 (*.json)|*.json" };
            if (picker.ShowDialog(this) != true) return;
            var preview = DataStore.ReadFile(picker.FileName);
            if (MessageBox.Show(this, $"备份包含 {preview.Tasks.Count} 条待办、{preview.Cards.Count} 张卡片和 {preview.History.Count} 条完成记录。\n\n恢复会替换当前数据；操作前会自动另存当前数据，可用同一入口还原。", "恢复完整备份", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _service.RestoreBackup(picker.FileName); RefreshData(); ReloadGeneral();
            Ui.ApplyTheme(_service.State.Preferences.Theme);
            _repositionDock();
        });
        var legacyHint = Ui.Text("旧版迁移：首次在旧程序旁启动时自动读取 Data。若新版放在其他位置，请选择旧版 Data 文件夹。源文件会保留，旧剪贴板历史不会自动加入常用内容。", 12, true); legacyHint.Margin = new Thickness(0, 20, 0, 8); panel.Children.Add(legacyHint);
        AddAction("导入旧版软件数据", () =>
        {
            var picker = new OpenFolderDialog { Title = "选择旧版 WinKit 的 Data 文件夹", Multiselect = false };
            if (picker.ShowDialog(this) == true) MessageBox.Show(this, $"已导入 {_service.ImportLegacy(picker.FolderName)} 条记录；相同来源不会重复导入。", "Levitation");
        });
        panel.Children.Insert(panel.Children.IndexOf(legacyHint), actions);
        return Ui.Scroll(panel);
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
            if (entries.Count == 0) list.Children.Add(Ui.Text(tasks ? "暂无删除的待办" : "暂无删除的常用内容", 12, true));
            foreach (var entry in entries)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var restore = Ui.Icon(Ui.Restore, "恢复", () => _service.RestoreTrash(entry.Id)); DockPanel.SetDock(restore, Dock.Right); row.Children.Add(restore);
                if (entry.ParentTaskId.HasValue)
                {
                    var parent = TaskTree.Find(_service.State.Tasks, entry.ParentTaskId.Value);
                    restore.ToolTip = parent != null && parent.Depth + 1 + TaskTree.Height(entry.Task!) <= TaskTree.MaximumDepth
                        ? "恢复到原待办下" : "原位置不存在或层级已满，恢复为普通待办";
                }
                var title = Ui.Text(entry.Title); title.MaxHeight = 45; title.TextTrimming = TextTrimming.CharacterEllipsis;
                var detail = Ui.Text((entry.ParentTaskId.HasValue ? $"子待办 · {entry.ParentTitle}\n" : "") + $"{entry.DeletedAt:yyyy-MM-dd HH:mm}", 11, true); detail.Margin = new Thickness(0, 4, 0, 0);
                row.Children.Add(Ui.Stack(title, detail)); var card = Ui.Card(row); card.Margin = new Thickness(0, 0, 0, 7); list.Children.Add(card);
            }
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
        if (records.Count == 0) _history.Children.Add(Ui.Text("这段时间还没有完成记录。", 12, true));
        var activeCompletions = _service.State.Tasks.Where(task => task.CompletedAt.HasValue && task.CompletionId.HasValue)
            .ToLookup(task => task.CompletionId!.Value);
        foreach (var group in records.GroupBy(h => h.Day))
        {
            var title = Ui.Text($"{group.Key:yyyy年M月d日}    {group.Count()} 项", 12); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 16, 0, 8); _history.Children.Add(title);
            foreach (var record in group)
            {
                var item = Ui.Text("✓  " + record.Title, 12); item.Margin = new Thickness(6, 7, 0, 7); item.ToolTip = record.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss"); _history.Children.Add(item);
                var subtasks = record.Subtasks;
                if (subtasks == null && activeCompletions[record.Id].FirstOrDefault() is { } task)
                    subtasks = TaskTree.CaptureSubtasks(task);
                if (subtasks == null) continue;
                foreach (var subtask in subtasks) _history.Children.Add(CreateHistorySubtaskRow(subtask));
            }
        }
    }

    private static Grid CreateHistorySubtaskRow(CompletionSubtask subtask)
    {
        var row = new Grid { MinHeight = 24, Margin = new Thickness(18 + (subtask.Depth - 1) * 14, 0, 8, 0) };
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
