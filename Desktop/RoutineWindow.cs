using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace Levitation.Desktop;

internal sealed class RoutineWindow : Window
{
    private static readonly DayOfWeek[] Weekdays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private readonly TaskService _service;
    private readonly TextBox _title = Ui.Input("任务内容");
    private readonly DateSelector _date = new();
    private readonly StackPanel _dateField, _weekdaysField;
    private readonly Dictionary<DayOfWeek, CheckBox> _dayButtons = new();
    private readonly RadioButton _once;
    private readonly UniformGrid _frequencyChoices;
    private readonly TextBlock _editorHeading = Heading("新增安排"), _error = Ui.Text("", 12);
    private readonly TextBlock _count = Ui.Text("", 12, true);
    private readonly StackPanel _list = new();
    private readonly Button _save, _cancel;
    private readonly Border _formSurface;
    private readonly DockPanel _scheduled;
    private readonly ScrollViewer _pageScroll, _listScroll;
    private readonly Grid _layout = new();
    private (Guid Id, string Title, string Detail)[] _listed = [];
    private Guid? _editing;
    private RepeatFrequency _frequency;

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
        _title.TextChanged += (_, _) => ClearError();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.Handled) return;
            e.Handled = true;
            App.Try(Save);
        };
        AddField("任务内容", _title);

        var choices = new UniformGrid { Columns = 3 };
        var choiceTemplate = ChoiceTemplate();
        var options = new[] { ("指定日期", RepeatFrequency.Once), ("每天", RepeatFrequency.Daily), ("每周", RepeatFrequency.Weekly) };
        foreach (var (label, frequency) in options)
        {
            var option = new RadioButton
            {
                Content = label, Tag = frequency, GroupName = "RoutineFrequency",
                Height = 34, Template = choiceTemplate,
                Margin = new Thickness(0, 0, choices.Children.Count < 2 ? 4 : 0, 0)
            };
            option.Checked += (_, _) => SelectFrequency(frequency);
            choices.Children.Add(option);
        }
        _once = (RadioButton)choices.Children[0];
        _frequencyChoices = choices;
        AddField("出现频率", choices);

        _dateField = AddField("任务日期", _date);
        var dayGrid = new UniformGrid { Columns = 7 };
        foreach (var day in Weekdays)
        {
            var label = "日一二三四五六"[(int)day].ToString();
            var button = new CheckBox { Content = label, Template = choiceTemplate, Height = 34,
                Margin = new Thickness(0, 0, day == DayOfWeek.Sunday ? 0 : 4, 0) };
            AutomationProperties.SetName(button, $"星期{label}");
            button.Checked += (_, _) => ClearError();
            button.Unchecked += (_, _) => ClearError();
            _dayButtons.Add(day, button);
            dayGrid.Children.Add(button);
        }
        _weekdaysField = AddField("每周哪几天", dayGrid);

        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        form.Children.Add(_error);
        _save = SettingsLayout.Action("添加任务", Save, primary: true);
        _cancel = SettingsLayout.Action("取消编辑", () => Reset(focus: true));
        _cancel.Margin = new Thickness(0, 0, 7, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(_cancel);
        actions.Children.Add(_save);
        form.Children.Add(actions);
        _formSurface = Ui.Card(form, new Thickness(14));
        _formSurface.SetResourceReference(Border.BackgroundProperty, "Paper");
        _formSurface.VerticalAlignment = VerticalAlignment.Top;
        _layout.Children.Add(_formSurface);

        _scheduled = new DockPanel();
        var listHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(_count, Dock.Right);
        listHeader.Children.Add(_count);
        listHeader.Children.Add(Heading("已安排"));
        DockPanel.SetDock(listHeader, Dock.Top);
        _scheduled.Children.Add(listHeader);
        _listScroll = Ui.Scroll(_list);
        _scheduled.Children.Add(_listScroll);
        _layout.Children.Add(_scheduled);
        _pageScroll = Ui.Scroll(_layout);
        Content = SettingsLayout.Page("日常任务", "到期的任务会自动出现在当天待办里。", _pageScroll);
        SizeChanged += (_, e) =>
        {
            ArrangeSections(e.NewSize.Width >= 720);
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
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = sideBySide ? new GridLength(320) : new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = sideBySide ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        if (sideBySide)
            _layout.ColumnDefinitions.Add(new ColumnDefinition());
        else
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(_scheduled, sideBySide ? 1 : 0);
        Grid.SetRow(_scheduled, sideBySide ? 0 : 1);
        _scheduled.Margin = sideBySide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 18, 0, 0);
        _pageScroll.ScrollToTop();
    }

    private void UpdateScrollMode(double height)
    {
        // Keep the form in view beside a long list. On narrow or short screens,
        // the entire page scrolls so the form and its actions remain reachable.
        bool separateList = _layout.ColumnDefinitions.Count == 2 && height >= 480;
        _pageScroll.VerticalScrollBarVisibility = separateList ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _listScroll.VerticalScrollBarVisibility = separateList ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private static TextBlock Heading(string text)
    {
        var heading = Ui.Text(text, 14);
        heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private static ControlTemplate ChoiceTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "Choice");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.SetResourceReference(TextElement.ForegroundProperty, "Ink");
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);
        template.VisualTree = border;
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlLine"), "Choice"));
            template.Triggers.Add(highlight);
        }
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Button"), "Choice"));
        selected.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("ButtonText"), "Choice"));
        template.Triggers.Add(selected);
        return template;
    }

    private void SelectFrequency(RepeatFrequency frequency)
    {
        _frequency = frequency;
        _dateField.Visibility = frequency == RepeatFrequency.Once ? Visibility.Visible : Visibility.Collapsed;
        _weekdaysField.Visibility = frequency == RepeatFrequency.Weekly ? Visibility.Visible : Visibility.Collapsed;
        if (frequency != RepeatFrequency.Once) _date.Close();
        ClearError();
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
        var days = Weekdays.Where(day => _dayButtons[day].IsChecked == true).ToList();
        if (_frequency == RepeatFrequency.Weekly && days.Count == 0)
        {
            ShowError("请选择至少一天。", _dayButtons[Weekdays[0]]);
            return;
        }
        var prior = _service.State.Routines.FirstOrDefault(rule => rule.Id == _editing);
        _service.SaveRoutine(new Routine
        {
            Id = prior?.Id ?? Guid.NewGuid(), Title = _title.Text, Frequency = _frequency,
            StartDate = _frequency == RepeatFrequency.Once ? _date.SelectedDate :
                prior?.Frequency == _frequency ? prior.StartDate : _service.Today,
            Weekdays = days, Extra = prior?.Extra
        });
        Reset(focus: true);
        RefreshList();
    }

    private void Reset(bool focus = false)
    {
        _editing = null;
        _title.Clear();
        _once.IsChecked = true;
        SelectFrequency(RepeatFrequency.Once);
        _date.SelectedDate = _service.Today;
        foreach (var (day, button) in _dayButtons) button.IsChecked = day == _service.Today.DayOfWeek;
        _editorHeading.Text = "新增安排";
        _save.Content = "添加任务";
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
        ((RadioButton)_frequencyChoices.Children[(int)rule.Frequency]).IsChecked = true;
        _date.SelectedDate = rule.StartDate;
        foreach (var (day, button) in _dayButtons) button.IsChecked = rule.Weekdays.Contains(day);
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
        var rows = _service.State.Routines.Select(rule => (rule.Id, rule.Title, rule.Description)).ToArray();
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
            return SettingsLayout.Row(rule.Title, rule.Description, actions, singleLineTitle: true);
        });
        _list.Children.Add(SettingsLayout.Surface(SettingsLayout.SeparatedRows(items)));
    }
}
