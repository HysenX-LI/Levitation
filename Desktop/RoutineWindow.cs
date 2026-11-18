using System.Windows.Controls.Primitives;

namespace Levitation.Desktop;

internal sealed class RoutineWindow : Window
{
    private readonly TaskService _service;
    private readonly TextBox _title = Ui.Input("日常任务内容");
    private readonly ComboBox _frequency = new();
    private readonly DateSelector _date = new();
    private readonly StackPanel _dateField;
    private readonly UniformGrid _fields = new() { Columns = 2, Margin = new Thickness(0, 14, 0, 12) };
    private readonly StackPanel _weekdays = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly Dictionary<DayOfWeek, CheckBox> _days = new();
    private readonly StackPanel _list = new();
    private readonly Button _save, _cancel;
    private Guid? _editing;

    public RoutineWindow(Window owner, TaskService service)
    {
        Ui.ThemeWindow(this);
        _service = service; Title = "Levitation · 日常任务"; Owner = owner; Width = 520; Height = Math.Min(620, SystemParameters.WorkArea.Height - 60);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; MinWidth = 430; MinHeight = 380; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var form = new StackPanel(); _title.Height = _title.MinHeight = 34;
        form.Children.Add(Ui.Field("任务内容", _title));
        _frequency.Items.Add("指定日期"); _frequency.Items.Add("每天"); _frequency.Items.Add("每周"); _frequency.SelectedIndex = 0; _frequency.Height = _frequency.MinHeight = 34;
        var fields = _fields;
        fields.Margin = new Thickness(0, 14, 0, 0);
        var left = Ui.Field("出现频率", _frequency); left.Margin = new Thickness(0, 0, 12, 0); fields.Children.Add(left);
        _dateField = Ui.Field("任务日期", _date); fields.Children.Add(_dateField); form.Children.Add(fields);
        var dayLabel = Ui.Text("重复日期", 11, true); dayLabel.Margin = new Thickness(0, 0, 0, 6); _weekdays.Children.Add(dayLabel);
        var dayGrid = new UniformGrid { Columns = 7 }; _weekdays.Children.Add(dayGrid);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var checkbox = new CheckBox { Content = "日一二三四五六"[(int)day].ToString(), Margin = new Thickness(0, 4, 0, 4), IsChecked = day == DateTime.Today.DayOfWeek };
            _days[day] = checkbox; dayGrid.Children.Add(checkbox);
        }
        form.Children.Add(_weekdays);
        _save = Ui.Action("添加任务", Save, true); _cancel = Ui.Action("取消编辑", Reset); _cancel.Visibility = Visibility.Collapsed;
        _save.MinWidth = _cancel.MinWidth = 88; _save.Height = _cancel.Height = 32;
        form.Children.Add(Ui.Buttons(_cancel, _save));
        panel.Children.Add(Ui.Card(form, new Thickness(16)));
        var hint = Ui.Text("到期自动进入当天待办。每天／每周任务按日期分别记录完成状态。", 11, true); hint.Margin = new Thickness(0, 10, 0, 20); panel.Children.Add(hint);
        var heading = Ui.Text("已安排的任务", 13); heading.FontWeight = FontWeights.SemiBold; panel.Children.Add(heading); panel.Children.Add(_list);
        Content = Ui.Scroll(panel);
        _frequency.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields(); RefreshList();
    }
    private void UpdateFields()
    {
        _weekdays.Visibility = _frequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _dateField.Visibility = _frequency.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        _fields.Columns = _frequency.SelectedIndex == 0 ? 2 : 1;
        ((FrameworkElement)_fields.Children[0]).Margin = new Thickness(0, 0, _frequency.SelectedIndex == 0 ? 12 : 0, 0);
        ((FrameworkElement)_fields.Children[0]).Width = _frequency.SelectedIndex == 0 ? double.NaN : 216;
        ((FrameworkElement)_fields.Children[0]).HorizontalAlignment = _frequency.SelectedIndex == 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (_frequency.SelectedIndex != 0) _date.Close();
    }
    private void Save()
    {
        var prior = _editing.HasValue ? _service.State.Routines.First(r => r.Id == _editing.Value) : null;
        _service.SaveRoutine(new Routine
        {
            Id = _editing ?? Guid.NewGuid(), Title = _title.Text, Frequency = (RepeatFrequency)_frequency.SelectedIndex,
            StartDate = _frequency.SelectedIndex == 0 ? _date.SelectedDate : prior?.Frequency == (RepeatFrequency)_frequency.SelectedIndex ? prior.StartDate : _service.Today,
            Weekdays = _days.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(), Extra = prior?.Extra
        });
        Reset(); RefreshList();
    }
    private void Reset()
    { _editing = null; _title.Clear(); _frequency.SelectedIndex = 0; _date.SelectedDate = _service.Today; _save.Content = "添加任务"; _cancel.Visibility = Visibility.Collapsed; UpdateFields(); }
    public void RefreshList()
    {
        _list.Children.Clear();
        if (_service.State.Routines.Count == 0) { var empty = Ui.Text("还没有日常任务。", 12, true); empty.Margin = new Thickness(0, 12, 0, 0); _list.Children.Add(empty); }
        foreach (var rule in _service.State.Routines)
        {
            var row = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Ui.Icon(Ui.Edit, "编辑安排", () =>
            {
                _editing = rule.Id; _title.Text = rule.Title; _frequency.SelectedIndex = (int)rule.Frequency;
                _date.SelectedDate = rule.StartDate;
                foreach (var pair in _days) pair.Value.IsChecked = rule.Weekdays.Contains(pair.Key);
                _save.Content = "保存修改"; _cancel.Visibility = Visibility.Visible; _title.Focus();
            }));
            actions.Children.Add(Ui.Icon(Ui.Delete, "删除安排，已生成的待办会保留", () =>
            { _service.DeleteRoutine(rule.Id); if (_editing == rule.Id) Reset(); }));
            DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
            var detail = Ui.Text(rule.Description, 11, true); detail.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(Ui.Stack(Ui.Text(rule.Title), detail));
            var card = Ui.Card(row); card.Margin = new Thickness(0, 8, 0, 0); _list.Children.Add(card);
        }
    }
}
