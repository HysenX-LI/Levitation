using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Levitation.Desktop;

// A compact month picker. The stored value remains a DateOnly, independent of presentation.
internal sealed class DateSelector : Grid
{
    private readonly Button _trigger;
    private readonly TextBlock _label = Ui.Text("");
    private readonly Popup _popup;
    private readonly UniformGrid _days = new() { Columns = 7 };
    private readonly ComboBox _month = new(), _year = new();
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _shown;
    private bool _updating;
    public event Action? DateChanged;

    public DateOnly SelectedDate
    {
        get => _selected;
        set
        {
            bool changed = _selected != value;
            _selected = value;
            _shown = new DateOnly(value.Year, value.Month, 1);
            _label.Text = value.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
            if (changed) DateChanged?.Invoke();
        }
    }

    public DateSelector()
    {
        _trigger = Ui.Action("", Open);
        _trigger.Margin = new Thickness(0); _trigger.Height = _trigger.MinHeight = 34; _trigger.Padding = new Thickness(10, 0, 10, 0);
        _trigger.SetResourceReference(Control.BackgroundProperty, "Surface"); _trigger.SetResourceReference(Control.BorderBrushProperty, "Line"); _trigger.BorderThickness = new Thickness(1);
        _trigger.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var field = new DockPanel { MinWidth = 150 };
        var icon = Ui.Text(Ui.Calendar); icon.FontFamily = Ui.IconFont; icon.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(icon, Dock.Right); field.Children.Add(icon); field.Children.Add(_label); _trigger.Content = field;
        AutomationProperties.SetName(_trigger, "选择任务日期"); Children.Add(_trigger);
        var panel = new StackPanel { Width = 280, Margin = new Thickness(12) };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition());
        _year.Margin = new Thickness(0, 0, 5, 10); _month.Margin = new Thickness(5, 0, 0, 10);
        _year.Height = _month.Height = _year.MinHeight = _month.MinHeight = 34;
        AutomationProperties.SetName(_year, "日历年份"); AutomationProperties.SetName(_month, "日历月份");
        for (int i = 1; i <= 12; i++) _month.Items.Add(i + "月");
        Grid.SetColumn(_month, 1); heading.Children.Add(_year); heading.Children.Add(_month); panel.Children.Add(heading);
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 4, 0, 4) };
        foreach (char day in "一二三四五六日") { var text = Ui.Text(day.ToString(), 11, true); text.HorizontalAlignment = HorizontalAlignment.Center; weekdays.Children.Add(text); }
        panel.Children.Add(weekdays); panel.Children.Add(_days);
        panel.Children.Add(Ui.Action("回到今天", () => Select(DateOnly.FromDateTime(DateTime.Today))));
        var surface = Ui.Card(panel, new Thickness(0)); surface.SetResourceReference(Border.BackgroundProperty, "Paper");
        surface.Margin = new Thickness(0, 5, 0, 5);
        _popup = new Popup { PlacementTarget = _trigger, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false,
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None, Child = surface };
        _popup.Closed += (_, _) => _trigger.Focus();
        _popup.Opened += (_, _) => FocusDay(_selected);
        panel.PreviewKeyDown += OnKey;
        _month.SelectionChanged += (_, _) => { if (!_updating && _month.SelectedIndex >= 0) { _shown = new DateOnly(_shown.Year, _month.SelectedIndex + 1, 1); Draw(); } };
        _year.SelectionChanged += (_, _) => { if (!_updating && _year.SelectedItem is int year) { _shown = new DateOnly(year, _shown.Month, 1); Draw(); } };
        SelectedDate = _selected;
        Unloaded += (_, _) => Close();
    }

    public void Close() => _popup.IsOpen = false;
    private void Open() { _shown = new DateOnly(_selected.Year, _selected.Month, 1); Draw(); _popup.IsOpen = true; FocusDay(_selected); }
    private void Select(DateOnly date) { SelectedDate = date; Close(); }
    private void Draw()
    {
        _updating = true;
        try
        {
            if (!_year.Items.Contains(_shown.Year)) { _year.Items.Clear(); for (int y = Math.Max(1, _shown.Year - 100); y <= Math.Min(9999, _shown.Year + 100); y++) _year.Items.Add(y); }
            _year.SelectedItem = _shown.Year; _month.SelectedIndex = _shown.Month - 1;
        }
        finally { _updating = false; }
        _days.Children.Clear();
        int offset = ((int)_shown.DayOfWeek + 6) % 7;
        for (int i = 0; i < offset; i++) _days.Children.Add(new Border());
        for (int day = 1; day <= DateTime.DaysInMonth(_shown.Year, _shown.Month); day++)
        {
            var date = new DateOnly(_shown.Year, _shown.Month, day);
            var button = Ui.Action(day.ToString(), () => Select(date), date == _selected);
            button.Tag = date; button.Height = 34; button.MinHeight = 34; button.Padding = new Thickness(0); button.Margin = new Thickness(2);
            if (date != _selected) button.Background = Brushes.Transparent;
            if (date == DateOnly.FromDateTime(DateTime.Today)) button.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetName(button, date.ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")));
            _days.Children.Add(button);
        }
    }
    private void FocusDay(DateOnly day) => _days.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, day))?.Focus();
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is not Button { Tag: DateOnly day }) return;
        DateOnly next;
        try
        {
            next = e.Key switch { Key.Left => day.AddDays(-1), Key.Right => day.AddDays(1), Key.Up => day.AddDays(-7), Key.Down => day.AddDays(7), Key.PageUp => day.AddMonths(-1), Key.PageDown => day.AddMonths(1), Key.Home => new DateOnly(day.Year, day.Month, 1), Key.End => new DateOnly(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month)), _ => day };
        }
        catch (ArgumentOutOfRangeException) { return; }
        if (next == day) return;
        _shown = new DateOnly(next.Year, next.Month, 1); Draw(); FocusDay(next); e.Handled = true;
    }
}
