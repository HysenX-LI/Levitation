using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace Levitation.Desktop;

internal sealed class RoutineScheduleEditor : StackPanel
{
    private static readonly DayOfWeek[] Days =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private readonly Func<DateOnly> _today;
    private readonly DateSelector _date = new();
    private readonly TextBlock _dateLabel = Ui.Text("任务日期", 11, true);
    private readonly ComboBox _frequency = new(), _unit = new(), _month = new(),
        _occurrence = new(), _weekday = new(), _missingDay = new();
    private readonly TextBox _interval = Ui.Input("重复间隔"), _monthDay = Ui.Input("日期（1–31）");
    private readonly Dictionary<DayOfWeek, CheckBox> _dayButtons = new();
    private readonly FrameworkElement _ruleSurface, _customFields, _weekdays, _monthly,
        _weekdayFields, _occurrenceField, _numberFields;
    private readonly TextBlock _hint = Ui.Text("", 11, true), _upcoming = Ui.Text("", 11, true);
    private bool _loading;
    public event Action? Changed;

    public RoutineScheduleEditor(Func<DateOnly> today)
    {
        _today = today;
        Margin = new Thickness(0, 12, 0, 0);
        _dateLabel.FontWeight = FontWeights.Medium;
        _dateLabel.Margin = new Thickness(0, 0, 0, 6);
        Fill<RepeatFrequency?>(_frequency, ("仅一次", RepeatFrequency.Once), ("每天", RepeatFrequency.Daily),
            ("仅工作日", RepeatFrequency.Workdays), ("每周", RepeatFrequency.Weekly),
            ("每月", RepeatFrequency.Monthly), ("每年", RepeatFrequency.Yearly), ("自定义…", (RepeatFrequency?)null));
        Children.Add(Pair(Ui.Stack(_dateLabel, _date), Field("重复", _frequency)));
        Fill(_unit, ("天", RepeatFrequency.Daily), ("周", RepeatFrequency.Weekly),
            ("个月", RepeatFrequency.Monthly), ("年", RepeatFrequency.Yearly));
        Fill(_month, ("第一天", MonthlyRule.FirstDay), ("最后一天", MonthlyRule.LastDay),
            ("第几个星期几", MonthlyRule.NthWeekday), ("最后一个星期几", MonthlyRule.LastWeekday), ("指定日号", MonthlyRule.DayOfMonth));
        Fill(_occurrence, Enumerable.Range(1, 5).Select(n => ($"第 {n} 个", n)).ToArray());
        Fill(_weekday, Days.Select(day => ($"星期{"日一二三四五六"[(int)day]}", day)).ToArray());
        Fill(_missingDay, ("跳过当月", MissingMonthDay.Skip), ("改到月末", MissingMonthDay.UseLastDay));
        _interval.Height = _interval.MinHeight = _monthDay.Height = _monthDay.MinHeight = 34;
        _interval.MaxLength = _monthDay.MaxLength = 2;
        _customFields = Pair(Field("每隔（1–99）", _interval), Field("间隔单位", _unit));

        var dayGrid = new UniformGrid { Columns = 7 };
        var template = WeekdayTemplate();
        foreach (var day in Days)
        {
            string label = "日一二三四五六"[(int)day].ToString();
            var button = new CheckBox { Content = label, Template = template, Height = 34,
                Margin = new Thickness(0, 0, day == DayOfWeek.Sunday ? 0 : 4, 0) };
            AutomationProperties.SetName(button, $"星期{label}");
            button.Checked += (_, _) => Update();
            button.Unchecked += (_, _) => Update();
            _dayButtons.Add(day, button);
            dayGrid.Children.Add(button);
        }
        _weekdays = Field("每周哪几天", dayGrid);
        _occurrenceField = Field("第几个", _occurrence);
        _weekdayFields = Pair(_occurrenceField, Field("星期", _weekday));
        _numberFields = Pair(Field("日期（1–31）", _monthDay), Field("当月没有该日", _missingDay));
        var monthFields = Ui.Stack(Field("月内规则", _month), _weekdayFields, _numberFields, _hint);
        _monthly = monthFields;
        _weekdayFields.Margin = _numberFields.Margin = new Thickness(0, 8, 0, 0);
        _hint.Margin = new Thickness(0, 6, 0, 0);
        var rules = Ui.Stack(_customFields, _weekdays, _monthly);
        foreach (UIElement element in rules.Children)
            if (element is FrameworkElement rule) rule.Margin = new Thickness(0, 3, 0, 3);
        var surface = SettingsLayout.Surface(rules);
        surface.Padding = new Thickness(10, 6, 10, 6);
        surface.Margin = new Thickness(0, 9, 0, 0);
        _ruleSurface = surface;
        Children.Add(surface);
        _upcoming.Margin = new Thickness(0, 7, 0, 0);
        AutomationProperties.SetLiveSetting(_upcoming, AutomationLiveSetting.Polite);
        Children.Add(_upcoming);

        foreach (var combo in new[] { _frequency, _unit, _month, _occurrence, _weekday, _missingDay })
            combo.SelectionChanged += (_, _) => Update();
        _date.DateChanged += Update;
        _interval.TextChanged += (_, _) => Update();
        _monthDay.TextChanged += (_, _) => Update();
        Load(null);
    }

    public void Load(Routine? rule)
    {
        _loading = true;
        _date.Close();
        _date.SelectedDate = rule?.StartDate ?? _today();
        var frequency = rule?.Frequency ?? RepeatFrequency.Once;
        Select(_frequency, rule is { Interval: > 1 } ? (RepeatFrequency?)null : frequency);
        Select(_unit, frequency is RepeatFrequency.Daily or RepeatFrequency.Monthly or RepeatFrequency.Yearly ? frequency : RepeatFrequency.Weekly);
        _interval.Text = (rule is { Interval: > 1 } ? rule.Interval : 2).ToString(CultureInfo.InvariantCulture);
        var monthly = rule?.Monthly ?? new MonthlySchedule(Day: _date.SelectedDate.Day);
        Select(_month, monthly.Rule);
        Select(_occurrence, monthly.Occurrence);
        Select(_weekday, monthly.Weekday);
        Select(_missingDay, monthly.MissingDay);
        _monthDay.Text = monthly.Day.ToString(CultureInfo.InvariantCulture);
        foreach (var (day, button) in _dayButtons)
            button.IsChecked = rule?.Weekdays.Contains(day) ?? day == _today().DayOfWeek;
        _loading = false;
        Update();
    }

    private bool Custom => (_frequency.SelectedItem as ComboBoxItem)?.Tag == null;
    private RepeatFrequency Frequency => Custom ? Value<RepeatFrequency>(_unit) : Value<RepeatFrequency>(_frequency);

    public bool TryRead(Routine rule, out string message, out Control? invalid)
    {
        message = "";
        invalid = null;
        rule.StartDate = _date.SelectedDate;
        rule.Frequency = Frequency;
        int interval = 1;
        if (Custom && !TryNumber(_interval, 99, out interval))
            return Invalid(_interval, "间隔请输入 1 到 99 之间的整数。", out message, out invalid);
        rule.Interval = interval;
        if (rule.Frequency == RepeatFrequency.Weekly)
        {
            rule.Weekdays = Days.Where(day => _dayButtons[day].IsChecked == true).ToList();
            if (rule.Weekdays.Count == 0)
                return Invalid(_dayButtons[Days[0]], "请选择至少一天。", out message, out invalid);
        }
        if (rule.Frequency == RepeatFrequency.Monthly)
        {
            var mode = Value<MonthlyRule>(_month);
            int day = 1;
            if (mode == MonthlyRule.DayOfMonth && !TryNumber(_monthDay, 31, out day))
                return Invalid(_monthDay, "请输入 1 到 31 之间的整数。", out message, out invalid);
            rule.Monthly = new MonthlySchedule(mode, day, Value<int>(_occurrence), Value<DayOfWeek>(_weekday), Value<MissingMonthDay>(_missingDay));
        }
        return true;
    }

    private void Update()
    {
        if (_loading) return;
        var frequency = Frequency;
        var mode = Value<MonthlyRule>(_month);
        Visible(_ruleSurface, Custom || frequency is RepeatFrequency.Weekly or RepeatFrequency.Monthly);
        Visible(_customFields, Custom);
        Visible(_weekdays, frequency == RepeatFrequency.Weekly);
        Visible(_monthly, frequency == RepeatFrequency.Monthly);
        Visible(_weekdayFields, mode is MonthlyRule.NthWeekday or MonthlyRule.LastWeekday);
        Visible(_occurrenceField, mode == MonthlyRule.NthWeekday);
        Visible(_numberFields, mode == MonthlyRule.DayOfMonth);
        // In the last-weekday rule the weekday selector takes the entire row.
        var fields = (Grid)_weekdayFields;
        fields.ColumnDefinitions[0].Width = mode == MonthlyRule.NthWeekday ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Visible(_hint, mode == MonthlyRule.NthWeekday && Value<int>(_occurrence) == 5);
        _hint.Text = "当月没有第 5 个所选星期时跳过。";
        _dateLabel.Text = frequency == RepeatFrequency.Once ? "任务日期" : "开始日期";
        Visible(_upcoming, frequency != RepeatFrequency.Once);
        if (frequency != RepeatFrequency.Once)
        {
            var rule = new Routine();
            var today = _today();
            var dates = TryRead(rule, out _, out _) ? rule.Upcoming(today).Select(date =>
                date.ToString(date.Year == today.Year ? "M月d日" : "yyyy年M月d日", CultureInfo.InvariantCulture)).ToArray() : [];
            _upcoming.Text = dates.Length == 0 ? "接下来  —" : "接下来  " + string.Join("、", dates);
        }
        Changed?.Invoke();
    }

    private static bool TryNumber(TextBox field, int maximum, out int number) =>
        int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 1 && number <= maximum;

    private static bool Invalid(Control control, string error, out string message, out Control? invalid)
    {
        message = error;
        invalid = control;
        return false;
    }

    private static void Visible(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Fill<T>(ComboBox combo, params (string Label, T Value)[] options)
    {
        foreach (var item in options) combo.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Value });
    }

    private static T Value<T>(ComboBox combo) => (T)((ComboBoxItem)combo.SelectedItem).Tag;
    private static void Select(ComboBox combo, object? value) =>
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, value));

    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        AutomationProperties.SetName(control, label);
        return Ui.Field(label, control);
    }

    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        left.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static ControlTemplate WeekdayTemplate()
    {
        var template = new ControlTemplate(typeof(CheckBox));
        var border = new FrameworkElementFactory(typeof(Border), "Day");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BackgroundProperty, "Paper");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Button"), "Day"));
        checkedTrigger.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, new DynamicResourceExtension("ButtonText"), "Day"));
        template.Triggers.Add(checkedTrigger);
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlLine"), "Day"));
            template.Triggers.Add(highlight);
        }
        return template;
    }
}
