using System.Windows.Automation;

namespace Levitation.Desktop;

internal static class SettingsLayout
{
    public static TabControl TabStrip()
    {
        var tabs = new TabControl();
        tabs.SetResourceReference(FrameworkElement.StyleProperty, "SettingsTabs");
        return tabs;
    }

    public static TabItem Tab(string title, UIElement content) => new() { Header = title, Content = content };

    public static UIElement Page(string title, string? description, UIElement body, UIElement? footer = null)
    {
        var page = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        var heading = Ui.Text(title, 21);
        heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        var header = Ui.Stack(heading);
        header.Margin = new Thickness(0, 0, 0, 13);
        if (description != null)
        {
            var hint = Ui.Text(description, 12, true);
            hint.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(hint);
        }
        DockPanel.SetDock(header, Dock.Top);
        page.Children.Add(header);
        if (footer != null)
        {
            var rule = new Border { Padding = new Thickness(0, 10, 0, 0),
                BorderThickness = new Thickness(0, 1, 0, 0), Child = footer, Margin = new Thickness(0, 10, 0, 0) };
            rule.SetResourceReference(Border.BorderBrushProperty, "Line");
            DockPanel.SetDock(rule, Dock.Bottom);
            page.Children.Add(rule);
        }
        page.Children.Add(body);
        return page;
    }

    public static Border Surface(UIElement child)
    {
        var surface = Ui.Card(child, new Thickness(0));
        surface.CornerRadius = new CornerRadius(8);
        return surface;
    }

    public static UIElement Section(string title, params UIElement[] rows)
    {
        var heading = Ui.Text(title, 12);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var section = Ui.Stack(heading, Surface(SeparatedRows(rows)));
        section.Margin = new Thickness(0, 0, 0, 13);
        return section;
    }

    public static StackPanel SeparatedRows(IEnumerable<UIElement> rows)
    {
        var list = new StackPanel();
        foreach (var row in rows)
        {
            if (list.Children.Count > 0)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(12, 0, 12, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "Line");
                list.Children.Add(divider);
            }
            list.Children.Add(row);
        }
        return list;
    }

    public static Grid Row(string title, string? description, FrameworkElement? trailing = null, bool singleLineTitle = false)
    {
        var row = new Grid { MinHeight = 40, Margin = new Thickness(12, 6, 12, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Ui.Text(title, 12);
        label.FontWeight = FontWeights.SemiBold;
        if (singleLineTitle)
        {
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.ToolTip = title;
        }
        var text = Ui.Stack(label);
        text.VerticalAlignment = VerticalAlignment.Center;
        if (description != null)
        {
            var details = Ui.Text(description, 11, true);
            details.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(details);
        }
        row.Children.Add(text);
        if (trailing != null)
        {
            var slot = new Border { Child = trailing, Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slot, 1);
            row.Children.Add(slot);
            if (trailing is Control && string.IsNullOrEmpty(AutomationProperties.GetName(trailing)))
                AutomationProperties.SetName(trailing, title);
        }
        return row;
    }

    public static Button Action(string label, Action action, bool primary = false, bool danger = false)
    {
        var button = Ui.Action(label, action, primary);
        button.Height = button.MinHeight = 30;
        button.FontSize = 12;
        button.Padding = new Thickness(10, 3, 10, 3);
        button.Margin = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        if (danger) button.SetResourceReference(Control.ForegroundProperty, "Danger");
        return button;
    }

    public static void Field(Control field, double width = double.NaN)
    {
        field.Height = field.MinHeight = 32;
        field.Width = width;
    }

    public static UIElement Empty(string message)
    {
        var text = Ui.Text(message, 12, true);
        text.Margin = new Thickness(12, 12, 12, 12);
        return Surface(text);
    }
}
