using System.Windows.Automation;

namespace Levitation.Desktop;

internal sealed class RoutineWindow : Window
{
    private readonly TaskService _service;
    private readonly TextBox _title = Ui.Input("任务内容");
    private readonly RoutineScheduleEditor _schedule;
    private readonly RoutineSubtaskEditor _subtasks;
    private readonly TextBlock _editorHeading = Heading("新增安排"), _error = Ui.Text("", 12);
    private readonly TextBlock _count = Ui.Text("", 12, true);
    private readonly StackPanel _list = new();
    private readonly Button _save, _cancel;
    private readonly FrameworkElement _formSurface;
    private readonly DockPanel _scheduled;
    private readonly ScrollViewer _pageScroll, _formScroll, _listScroll;
    private readonly Grid _layout = new();
    private (Guid Id, string Title, string Detail, int Subtasks)[] _listed = [];
    private Guid? _editing;

    public RoutineWindow(Window owner, TaskService service)
    {
        Ui.ThemeWindow(this);
        _service = service;
        Title = "Levitation · 日常任务";
        Owner = owner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var form = new StackPanel();
        form.Children.Add(_editorHeading);
        _title.Height = _title.MinHeight = 34;
        _title.Padding = new Thickness(10, 3, 10, 3);
        bool composingTitle = false;
        TextCompositionManager.AddPreviewTextInputStartHandler(_title, (_, _) => composingTitle = true);
        _title.PreviewTextInput += (_, _) => composingTitle = false;
        _title.LostKeyboardFocus += (_, _) => composingTitle = false;
        _title.TextChanged += (_, _) => ClearError();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.Handled || composingTitle) return;
            e.Handled = true;
            App.Try(Save);
        };
        AddField("任务内容", _title);

        _schedule = new RoutineScheduleEditor(() => _service.Today);
        _schedule.Changed += ClearError;
        form.Children.Add(_schedule);
        _subtasks = new RoutineSubtaskEditor(this, () => string.IsNullOrWhiteSpace(_title.Text) ? "当前安排" : _title.Text.Trim());
        form.Children.Add(_subtasks);

        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        form.Children.Add(_error);
        _save = SettingsLayout.Action("添加安排", Save, primary: true);
        _cancel = SettingsLayout.Action("取消编辑", () => Reset(focus: true));
        _cancel.Margin = new Thickness(0, 0, 7, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(_cancel);
        actions.Children.Add(_save);
        form.Children.Add(actions);
        var editor = new Border { Child = form };
        _formSurface = editor;
        _formScroll = new DockScrollViewer { Content = editor };
        _layout.Children.Add(_formScroll);

        _scheduled = new DockPanel();
        var listHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(_count, Dock.Right);
        listHeader.Children.Add(_count);
        listHeader.Children.Add(Heading("已安排"));
        DockPanel.SetDock(listHeader, Dock.Top);
        _scheduled.Children.Add(listHeader);
        _listScroll = new DockScrollViewer { Content = _list };
        _scheduled.Children.Add(_listScroll);
        _layout.Children.Add(_scheduled);
        _pageScroll = new DockScrollViewer { Content = _layout };
        Content = SettingsLayout.Page("日常任务", "到期的任务会自动出现在当天待办里。", _pageScroll);
        SizeChanged += (_, e) =>
        {
            ArrangeSections(e.NewSize.Width >= 690);
            UpdateScrollMode(e.NewSize.Height);
        };
        ArrangeSections(false);
        UpdateScrollMode(0);
        Reset();
        RefreshList();

        StackPanel AddField(string label, UIElement control)
        {
            var field = Ui.Field(label, control);
            field.Margin = new Thickness(0, 12, 0, 0);
            form.Children.Add(field);
            return field;
        }
    }

    private void ArrangeSections(bool sideBySide)
    {
        if ((_layout.ColumnDefinitions.Count == 2) == sideBySide && _layout.RowDefinitions.Count > 0) return;
        _layout.ColumnDefinitions.Clear();
        _layout.RowDefinitions.Clear();
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = sideBySide ? new GridLength(1.42, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = sideBySide ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        if (sideBySide)
            _layout.ColumnDefinitions.Add(new ColumnDefinition());
        else
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(_scheduled, sideBySide ? 1 : 0);
        Grid.SetRow(_scheduled, sideBySide ? 0 : 1);
        _scheduled.Margin = sideBySide ? new Thickness(18, 0, 0, 0) : new Thickness(0, 18, 0, 0);
        _pageScroll.ScrollToTop();
    }

    private void UpdateScrollMode(double height)
    {
        // Keep the form in view beside a long list. On narrow or short screens,
        // the entire page scrolls so the form and its actions remain reachable.
        bool separateList = _layout.ColumnDefinitions.Count == 2 && height >= 480;
        _pageScroll.VerticalScrollBarVisibility = separateList ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _formScroll.VerticalScrollBarVisibility = separateList ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _listScroll.VerticalScrollBarVisibility = separateList ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private static TextBlock Heading(string text)
    {
        var heading = Ui.Text(text, 14);
        heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private void ClearError() => _error.Visibility = Visibility.Collapsed;

    private void ShowError(string message, Control focus)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        focus.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_title.Text)) { ShowError("请输入任务内容。", _title); return; }
        var prior = _service.State.Routines.FirstOrDefault(rule => rule.Id == _editing);
        var routine = new Routine { Id = prior?.Id ?? Guid.NewGuid(), Title = _title.Text, Extra = prior?.Extra };
        if (!_schedule.TryRead(routine, out var message, out var invalid))
        {
            ShowError(message, invalid!);
            return;
        }
        routine.Subtasks = _subtasks.Collect();
        _service.SaveRoutine(routine);
        Reset(focus: true);
        RefreshList();
    }

    private void Reset(bool focus = false)
    {
        _editing = null;
        _title.Clear();
        _schedule.Load(null);
        _subtasks.Load([]);
        _editorHeading.Text = "新增安排";
        _save.Content = "添加安排";
        _cancel.Visibility = Visibility.Collapsed;
        if (focus) _title.Focus();
    }

    private void Edit(Guid id)
    {
        var rule = _service.State.Routines.FirstOrDefault(routine => routine.Id == id);
        if (rule == null) return;
        _editing = rule.Id;
        _editorHeading.Text = "编辑安排";
        _title.Text = rule.Title;
        _schedule.Load(rule);
        _subtasks.Load(rule.Subtasks);
        _save.Content = "保存修改";
        _cancel.Visibility = Visibility.Visible;
        ClearError();
        _formSurface.BringIntoView();
        _title.Focus();
        _title.SelectAll();
    }

    public void RefreshList()
    {
        if (_editing is Guid edit && !_service.State.Routines.Any(rule => rule.Id == edit)) Reset();
        static int Count(IEnumerable<RoutineSubtask> children) => children.Sum(child => 1 + Count(child.Children));
        var rows = _service.State.Routines.Select(rule =>
            (rule.Id, rule.Title, Detail: rule.Description, Subtasks: Count(rule.Subtasks))).ToArray();
        if (rows.SequenceEqual(_listed) && _list.Children.Count > 0) return;
        _listed = rows;
        _count.Text = $"{rows.Length} 项";
        _list.Children.Clear();
        if (rows.Length == 0)
        {
            _list.Children.Add(SettingsLayout.Empty("还没有安排。"));
            return;
        }

        var items = rows.Select(rule =>
        {
            var edit = Ui.Icon(Ui.Edit, "编辑安排", () => Edit(rule.Id));
            var delete = Ui.Icon(Ui.Delete, "删除安排，保留已生成的待办", () => _service.DeleteRoutine(rule.Id));
            AutomationProperties.SetName(edit, $"编辑安排 {rule.Title}");
            AutomationProperties.SetName(delete, $"删除安排 {rule.Title}");
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(edit);
            actions.Children.Add(delete);
            string detail = rule.Subtasks == 0 ? rule.Detail : rule.Detail + $"\n{rule.Subtasks} 个子待办";
            var row = SettingsLayout.Row(rule.Title, detail, actions, singleLineTitle: true);
            var surface = new Border { Child = row, CornerRadius = new CornerRadius(7),
                Margin = new Thickness(3, 2, 3, 2), Background = Brushes.Transparent };
            surface.MouseEnter += (_, _) => surface.SetResourceReference(Border.BackgroundProperty, "MenuHover");
            surface.MouseLeave += (_, _) => surface.Background = Brushes.Transparent;
            surface.MouseLeftButtonUp += (_, _) => { if (!actions.IsMouseOver) Edit(rule.Id); };
            return surface;
        });
        _list.Children.Add(SettingsLayout.Surface(SettingsLayout.SeparatedRows(items)));
    }
}
