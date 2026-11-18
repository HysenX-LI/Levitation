using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Levitation.Desktop;

internal static class Ui
{
    public const string Copy = "\uE8C8", Delete = "\uE74D", Edit = "\uE70F", Settings = "\uE713",
        Pin = "\uE718", Arrow = "\uE76C", Calendar = "\uE787", Restore = "\uE777", Link = "\uE8A7";
    public static readonly FontFamily Font = new("Segoe UI, Microsoft YaHei UI");
    public static readonly FontFamily TaskFont = new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.Resources[key];
    private static bool _dark;
    private static readonly HashSet<ContextMenu> _openMenus = new();
    public static event Action? ThemeChanged;
    public static bool TrayMenuOpen { get; set; }
    public static bool HasOpenMenu { get { _openMenus.RemoveWhere(menu => !menu.IsOpen); return TrayMenuOpen || _openMenus.Count > 0; } }

    public static ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => { _openMenus.Add(menu); CommandManager.InvalidateRequerySuggested(); };
        menu.Closed += (_, _) => _openMenus.Remove(menu);
        return menu;
    }
    public static void BringForward(Window window)
    {
        if (window.WindowState != WindowState.Normal) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate(); window.Focus();
    }

    public static void ThemeWindow(Window window)
    {
        // An implicit Window style is not looked up for a derived window type.
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        window.SourceInitialized += (_, _) => NativeWindow.ThemeTitleBar(window, _dark);
    }

    public static void ApplyTheme(string mode)
    {
        bool dark = mode == "Dark";
        if (mode == "System")
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        // Neutral Gray steps from https://github.com/radix-ui/colors.
        // Semantic foreground/background pairs follow shadcn/ui's theming convention.
        var colors = dark
            ? new Dictionary<string, string> { ["Paper"] = "#111111", ["Surface"] = "#191919", ["Ink"] = "#EEEEEE", ["Muted"] = "#B4B4B4", ["Line"] = "#3A3A3A", ["ControlLine"] = "#7B7B7B", ["Soft"] = "#222222", ["Accent"] = "#EEEEEE", ["Button"] = "#EEEEEE", ["ButtonText"] = "#202020", ["Danger"] = "#FF9592" }
            : new Dictionary<string, string> { ["Paper"] = "#FFFFFF", ["Surface"] = "#F9F9F9", ["Ink"] = "#202020", ["Muted"] = "#646464", ["Line"] = "#D9D9D9", ["ControlLine"] = "#8D8D8D", ["Soft"] = "#F0F0F0", ["Accent"] = "#202020", ["Button"] = "#202020", ["ButtonText"] = "#FFFFFF", ["Danger"] = "#CE2C31" };
        foreach (var (name, color) in colors)
            System.Windows.Application.Current.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        System.Windows.Application.Current.Resources["ScrollThumb"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#686868" : "#909090"));
        System.Windows.Application.Current.Resources["ScrollThumbHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#858585" : "#707070"));
        System.Windows.Application.Current.Resources["MenuHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#303030" : "#EAEAEA"));
        System.Windows.Application.Current.Resources["TraySurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#191919" : "#FFFFFF"));
        System.Windows.Application.Current.Resources["TrayLine"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#383838" : "#DEDEDE"));
        System.Windows.Application.Current.Resources["TrayIcon"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#B4B4B4" : "#737373"));
        System.Windows.Application.Current.Resources["TaskInk"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#D0D0D0" : "#3C3C3C"));
        System.Windows.Application.Current.Resources["TaskHoverInk"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FFFFFF" : "#111111"));
        System.Windows.Application.Current.Resources["TaskHoverSurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2D2D2D" : "#EAEAEA"));
        System.Windows.Application.Current.Resources["GuideLine"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2F2F2F" : "#E4E4E4"));
        _dark = dark;
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, token) in new[] {
            (SystemColors.WindowBrushKey, "Paper"), (SystemColors.ControlBrushKey, "Paper"),
            (SystemColors.WindowTextBrushKey, "Ink"), (SystemColors.ControlTextBrushKey, "Ink"),
            (SystemColors.HighlightBrushKey, "Button"), (SystemColors.HighlightTextBrushKey, "ButtonText") })
            resources[key] = resources[token];
        foreach (Window window in System.Windows.Application.Current.Windows) NativeWindow.ThemeTitleBar(window, dark);
        ThemeChanged?.Invoke();
    }

    public static void InstallStyles()
    {
        var resources = System.Windows.Application.Current.Resources;
        var window = new Style(typeof(Window));
        window.Setters.Add(new Setter(Control.FontFamilyProperty, Font));
        window.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        window.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
        window.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Paper")));
        window.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        resources[typeof(Window)] = window;

        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Soft")));
        button.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        button.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        button.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); template.VisualTree = border;
        button.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .75)); button.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); button.Triggers.Add(disabled);
        resources[typeof(Button)] = button;

        var text = new Style(typeof(TextBox));
        text.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        text.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Surface")));
        text.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        text.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Line")));
        text.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        text.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        text.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, new DynamicResourceExtension("Ink")));
        text.Setters.Add(new Setter(Control.TemplateProperty, InputTemplate()));
        resources[typeof(TextBox)] = text;
        InstallCheckBoxStyle(resources);
        InstallTabStyle(resources);
        InstallComboBoxStyle(resources);
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Levitation;component/Desktop/ScrollStyles.xaml", UriKind.Relative) });
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Levitation;component/Desktop/MenuStyles.xaml", UriKind.Relative) });
    }

    private static void InstallComboBoxStyle(ResourceDictionary resources)
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Surface")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Line")));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34d));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty, 280d));
        var template = new ControlTemplate(typeof(ComboBox));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(Control.FocusableProperty, false); toggle.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var background = new FrameworkElementFactory(typeof(Border), "FieldBorder");
        background.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); background.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        background.SetResourceReference(Border.BackgroundProperty, "Surface"); background.SetResourceReference(Border.BorderBrushProperty, "Line");
        toggleTemplate.VisualTree = background;
        foreach (var property in new[] { UIElement.IsMouseOverProperty, ToggleButton.IsCheckedProperty })
        {
            var active = new Trigger { Property = property, Value = true };
            active.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlLine"), "FieldBorder")); toggleTemplate.Triggers.Add(active);
        }
        toggle.SetValue(Control.TemplateProperty, toggleTemplate); grid.AppendChild(toggle);
        var value = new FrameworkElementFactory(typeof(ContentPresenter));
        value.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 5, 36, 5)); value.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); value.SetValue(UIElement.IsHitTestVisibleProperty, false);
        value.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = RelativeSource.TemplatedParent });
        value.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = RelativeSource.TemplatedParent }); grid.AppendChild(value);
        var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "ComboArrow");
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0,0 L 4,4 L 8,0"));
        arrow.SetValue(FrameworkElement.WidthProperty, 8d); arrow.SetValue(FrameworkElement.HeightProperty, 4d);
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.3); arrow.SetValue(System.Windows.Shapes.Shape.StretchProperty, Stretch.Uniform);
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round); arrow.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 13, 0)); arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right); arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(UIElement.IsHitTestVisibleProperty, false); arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        arrow.SetValue(UIElement.RenderTransformProperty, new RotateTransform()); arrow.SetValue(UIElement.RenderTransformOriginProperty, new Point(.5, .5)); grid.AppendChild(arrow);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup"); popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom); popup.SetValue(Popup.AllowsTransparencyProperty, true); popup.SetValue(UIElement.FocusableProperty, false);
        popup.SetValue(Popup.PopupAnimationProperty, SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent });
        var popupBorder = new FrameworkElementFactory(typeof(Border)); popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1)); popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 5)); popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetResourceReference(Border.BackgroundProperty, "Surface"); popupBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer)); scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto); scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetBinding(FrameworkElement.MaxHeightProperty, new Binding("MaxDropDownHeight") { RelativeSource = RelativeSource.TemplatedParent });
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); popupBorder.AppendChild(scroll); popup.AppendChild(popupBorder); grid.AppendChild(popup); template.VisualTree = grid;
        var opened = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
        if (SystemParameters.ClientAreaAnimation)
        {
            foreach (var (actions, from, to) in new[] { (opened.EnterActions, 0d, 180d), (opened.ExitActions, 180d, 0d) })
            {
                var storyboard = new Storyboard(); var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(140));
                Storyboard.SetTargetName(animation, "ComboArrow"); Storyboard.SetTargetProperty(animation, new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, RotateTransform.AngleProperty)); storyboard.Children.Add(animation); actions.Add(new BeginStoryboard { Storyboard = storyboard });
            }
        }
        template.Triggers.Add(opened); style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); style.Triggers.Add(disabled);
        resources[typeof(ComboBox)] = style;
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        var itemTemplate = new ControlTemplate(typeof(ComboBoxItem)); var itemBorder = new FrameworkElementFactory(typeof(Border)); itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        itemBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent }); itemBorder.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter))); itemTemplate.VisualTree = itemBorder; itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
        foreach (var property in new[] { ComboBoxItem.IsHighlightedProperty, ComboBoxItem.IsSelectedProperty }) { var active = new Trigger { Property = property, Value = true }; active.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("MenuHover"))); itemStyle.Triggers.Add(active); }
        resources[typeof(ComboBoxItem)] = itemStyle;
    }

    private static void InstallCheckBoxStyle(ResourceDictionary resources)
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        var template = new ControlTemplate(typeof(CheckBox));
        var row = new FrameworkElementFactory(typeof(DockPanel)); row.SetValue(DockPanel.LastChildFillProperty, true);
        var square = new FrameworkElementFactory(typeof(Border), "CheckSquare");
        square.SetValue(FrameworkElement.WidthProperty, 16d); square.SetValue(FrameworkElement.HeightProperty, 16d);
        square.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        square.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); square.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        square.SetResourceReference(Border.BorderBrushProperty, "ControlLine"); square.SetResourceReference(Border.BackgroundProperty, "Surface");
        var check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "CheckMark");
        check.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 1,4.5 L 4,7.5 L 10,1.5"));
        check.SetValue(FrameworkElement.WidthProperty, 10d); check.SetValue(FrameworkElement.HeightProperty, 8d);
        check.SetValue(System.Windows.Shapes.Shape.StretchProperty, Stretch.Uniform); check.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.6);
        check.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round); check.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round); check.SetValue(System.Windows.Shapes.Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        check.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink"); square.AppendChild(check); row.AppendChild(square);
        var label = new FrameworkElementFactory(typeof(ContentPresenter)); label.SetValue(ContentPresenter.ContentSourceProperty, "Content"); label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(label);
        template.VisualTree = row;
        var selected = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "CheckSquare"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent, "CheckSquare"));
        selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark")); template.Triggers.Add(selected);
        var hover = new MultiTrigger(); hover.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true)); hover.Conditions.Add(new System.Windows.Condition(ToggleButton.IsCheckedProperty, false));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Ink"), "CheckSquare")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "CheckSquare")); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); resources[typeof(CheckBox)] = style;
    }

    private static void InstallTabStyle(ResourceDictionary resources)
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Paper")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Muted")));
        var template = new ControlTemplate(typeof(TabItem));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        var header = new FrameworkElementFactory(typeof(ContentPresenter)); header.SetValue(ContentPresenter.ContentSourceProperty, "Header"); border.AppendChild(header);
        template.VisualTree = border; style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Soft")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink"))); style.Triggers.Add(selected);
        resources[typeof(TabItem)] = style;
    }

    public static TextBlock Text(string value, double size = 12, bool muted = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
        return text;
    }
    public static Button Action(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), MinHeight = 32 };
        if (primary) { button.SetResourceReference(Control.BackgroundProperty, "Button"); button.SetResourceReference(Control.ForegroundProperty, "ButtonText"); }
        button.Click += (_, _) => App.Try(action);
        return button;
    }
    public static Button Icon(string glyph, string label, Action action)
    {
        var button = Action(glyph, action); button.FontFamily = IconFont; button.FontSize = 13;
        button.Width = 27; button.Height = 27; button.MinHeight = 0; button.Padding = new Thickness(3);
        button.Margin = new Thickness(1); button.Background = Brushes.Transparent; button.ToolTip = label;
        AutomationProperties.SetName(button, label); return button;
    }
    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var border = new Border { Child = child, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(10, 4, 10, 4) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); return border;
    }
    public static ControlTemplate FlatToggleTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var area = new FrameworkElementFactory(typeof(Border)); area.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        area.AppendChild(presenter); template.VisualTree = area; return template;
    }
    public static (Border Surface, ToggleButton Toggle) Section(UIElement heading, UIElement content, UIElement? tools, string label,
        bool expanded, Func<double> height, double minimum, Action<bool> commit, Action<double> resizeCommit)
    {
        if (heading is TextBlock title)
        {
            title.FontFamily = TaskFont; title.LineHeight = 20; title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.Padding = new Thickness(0, 1, 0, 2); title.TextWrapping = TextWrapping.NoWrap;
        }
        var body = new Border { Child = content, Margin = new Thickness(0, 8, 0, 0), Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        var arrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 L 4,4 L 0,8"), Width = 4, Height = 8, Stretch = Stretch.Uniform, StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        var arrowBox = new Border { Child = arrow, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        arrow.RenderTransformOrigin = new Point(.5, .5); arrow.RenderTransform = new RotateTransform(expanded ? 90 : 0);
        var caption = new StackPanel { Orientation = Orientation.Horizontal }; caption.Children.Add(arrowBox); caption.Children.Add(heading);
        var toggle = new ToggleButton { Content = caption, IsChecked = expanded, Cursor = Cursors.Hand, MinHeight = 32, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(toggle, label + "，展开或折叠");
        toggle.Template = FlatToggleTemplate();
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(toggle); if (tools != null) { Grid.SetColumn(tools, 1); header.Children.Add(tools); }
        var resize = new Thumb { Height = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-12, 0, -12, -6), Cursor = Cursors.SizeNS, Focusable = false, ToolTip = "上下拖动调整" + label + "卡片高度", Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(resize, "调整" + label + "卡片高度");
        var resizeTemplate = new ControlTemplate(typeof(Thumb));
        var hitArea = new FrameworkElementFactory(typeof(Border)); hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var mark = new FrameworkElementFactory(typeof(Border)); mark.SetValue(FrameworkElement.WidthProperty, 24d); mark.SetValue(FrameworkElement.HeightProperty, 2d);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); mark.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));
        mark.SetValue(Border.CornerRadiusProperty, new CornerRadius(1)); mark.SetResourceReference(Border.BackgroundProperty, "ControlLine"); mark.SetValue(UIElement.OpacityProperty, .4);
        hitArea.AppendChild(mark); resizeTemplate.VisualTree = hitArea; resize.Template = resizeTemplate;
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(header); Grid.SetRow(body, 1); panel.Children.Add(body); Grid.SetRowSpan(resize, 2); panel.Children.Add(resize);
        var surface = Card(panel, new Thickness(12, 8, 12, 6)); surface.Height = expanded ? height() : double.NaN;
        double before = 0;
        resize.DragStarted += (_, _) => before = surface.Height;
        resize.DragDelta += (_, e) => surface.Height = Math.Clamp(surface.Height + e.VerticalChange, minimum, 1200);
        resize.DragCompleted += (_, e) =>
        {
            if (e.Canceled) surface.Height = before;
            else App.Try(() => { try { resizeCommit(surface.Height); } catch { surface.Height = before; throw; } });
        };
        void Changed()
        {
            bool open = toggle.IsChecked == true;
            body.Visibility = resize.Visibility = open ? Visibility.Visible : Visibility.Collapsed; surface.Height = open ? height() : double.NaN; arrow.RenderTransform = new RotateTransform(open ? 90 : 0);
            App.Try(() => { try { commit(open); } catch { toggle.IsChecked = !open; throw; } });
        }
        toggle.Checked += (_, _) => Changed(); toggle.Unchecked += (_, _) => Changed();
        return (surface, toggle);
    }
    public static StackPanel Stack(params UIElement[] children)
    { var stack = new StackPanel(); foreach (var child in children) stack.Children.Add(child); return stack; }
    public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
    public static TextBox Input(string label, string value = "", bool multiline = false)
    {
        var text = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 112 : 34, MaxHeight = multiline ? 220 : double.PositiveInfinity, MaxLength = multiline ? 20000 : 1000, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        var menu = CreateMenu();
        foreach (var (caption, command, shortcut) in new[] { ("剪切", ApplicationCommands.Cut, "Ctrl+X"), ("复制", ApplicationCommands.Copy, "Ctrl+C"), ("粘贴", ApplicationCommands.Paste, "Ctrl+V"), ("全选", ApplicationCommands.SelectAll, "Ctrl+A") })
        {
            if (command == ApplicationCommands.SelectAll) menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = caption, Command = command, CommandTarget = text, InputGestureText = shortcut });
        }
        text.ContextMenu = menu;
        AutomationProperties.SetName(text, label); return text;
    }
    public static void RoundInput(TextBox input)
    { input.Template = InputTemplate(); input.MinHeight = 30; input.Height = 30; input.Padding = new Thickness(8, 2, 8, 2); }
    public static ControlTemplate InputTemplate(bool decorate = true)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var border = new FrameworkElementFactory(typeof(Border), "InputBorder");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        foreach (var (property, source) in new[] {
            (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"),
            (Border.BorderThicknessProperty, "BorderThickness") })
            border.SetBinding(property, new Binding(source) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        content.SetValue(UIElement.FocusableProperty, false);
        // TextBoxBase forwards Padding to its content host. Applying it to the
        // outer border as well consumes the text viewport twice.
        content.SetBinding(Control.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ScrollViewer.VerticalScrollBarVisibilityProperty, new Binding("VerticalScrollBarVisibility") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ScrollViewer.HorizontalScrollBarVisibilityProperty, new Binding("HorizontalScrollBarVisibility") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(content); grid.AppendChild(border);
        if (!decorate) { template.VisualTree = grid; return template; }
        var ring = new FrameworkElementFactory(typeof(Border), "FocusRing"); ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); ring.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        ring.SetResourceReference(Border.BorderBrushProperty, "ControlLine"); ring.SetValue(UIElement.IsHitTestVisibleProperty, false); ring.SetValue(UIElement.OpacityProperty, 0d); grid.AppendChild(ring);
        template.VisualTree = grid;
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, "FocusRing"));
        if (SystemParameters.ClientAreaAnimation)
        {
            var storyboard = new Storyboard(); var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop };
            Storyboard.SetTargetName(fade, "FocusRing"); Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty)); storyboard.Children.Add(fade); focus.EnterActions.Add(new BeginStoryboard { Storyboard = storyboard });
        }
        template.Triggers.Add(focus);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlLine"), "InputBorder")); template.Triggers.Add(hover);
        var readOnly = new Trigger { Property = TextBoxBase.IsReadOnlyProperty, Value = true }; readOnly.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Soft"), "InputBorder")); template.Triggers.Add(readOnly);
        return template;
    }
    public static void Label(Panel parent, string label, UIElement control)
    {
        var field = Field(label, control); field.Margin = new Thickness(0, 14, 0, 0); parent.Children.Add(field);
    }
    public static StackPanel Field(string label, UIElement control)
    {
        var text = Text(label, 11, true); text.FontWeight = FontWeights.Medium; text.Margin = new Thickness(0, 0, 0, 6);
        return Stack(text, control);
    }
    public static Window Dialog(string title, Window owner, double width = 440)
    {
        var window = new Window { Title = title, Owner = owner, Width = width, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 60), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        ThemeWindow(window); return window;
    }
    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var button in buttons) panel.Children.Add(button); return panel;
    }
}
