using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media.Animation;
using Shape = System.Windows.Shapes.Shape;
using Path = System.Windows.Shapes.Path;

namespace Levitation.Desktop;

internal static class TaskControls
{
    public const string Play = "M 5,2.5 L 13,8 L 5,13.5 Z", Pause = "M 5.5,3 V 13 M 10.5,3 V 13",
        Check = "M 2.5,8 L 6,11.5 L 13.5,4", Trash = "M 3,4.5 H 13 M 6,2.5 H 10 M 4,4.5 L 5,13.5 H 11 L 12,4.5 M 7,7 V 11 M 9,7 V 11",
        Plus = "M 8,3.5 V 12.5 M 3.5,8 H 12.5", Enter = "M 13,3 V 8 A 2,2 0 0 1 11,10 H 3 M 6,7 L 3,10 L 6,13";

    public static Button Button(UIElement mark, string label, string title, Action action, bool left = false, bool emphasis = false)
    {
        var button = Ui.Action(label, action);
        button.Content = mark; button.Width = 24; button.Height = 28; button.MinHeight = 0;
        button.Margin = new Thickness(0); button.Padding = new Thickness(0); button.Opacity = left ? 1 : .75;
        button.FocusVisualStyle = null;
        button.HorizontalContentAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        button.Background = Brushes.Transparent; button.ToolTip = label;
        button.SetResourceReference(Control.ForegroundProperty, emphasis ? "Ink" : "Muted");
        System.Windows.Automation.AutomationProperties.SetName(button, label + "：" + title);
        var template = new ControlTemplate(typeof(Button));
        var area = new FrameworkElementFactory(typeof(Border), "Area");
        area.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent }); area.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        area.AppendChild(content); template.VisualTree = area;
        if (!left)
        {
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Paper"), "Area")); template.Triggers.Add(hover);
        }
        if (!left)
        {
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlLine"), "Area"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "Area")); template.Triggers.Add(focus);
        }
        else if (mark is Border square)
            button.IsKeyboardFocusWithinChanged += (_, _) => square.SetResourceReference(Border.BorderBrushProperty, button.IsKeyboardFocusWithin ? "Ink" : "ControlLine");
        button.Template = template;
        return button;
    }

    public static UIElement Glyph(string data, double size = 14)
    {
        // A fixed 16-unit canvas preserves the same whitespace as the preview's
        // SVG viewBox; stretching the path alone would enlarge narrow glyphs.
        var canvas = new Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        var path = Stroke(data, null, 1.5);
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }
    public static Border EmptyBox()
    {
        var box = new Border { Width = 16, Height = 16, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        box.SetResourceReference(Border.BorderBrushProperty, "ControlLine"); box.SetResourceReference(Border.BackgroundProperty, "Surface"); return box;
    }
    internal static Path Stroke(string data, string? resource, double thickness)
    {
        var path = new Path { Width = 16, Height = 16, Data = Geometry.Parse(data), Stretch = Stretch.None, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        if (resource != null) path.SetResourceReference(Shape.StrokeProperty, resource);
        return path;
    }
}

internal sealed class TaskProgressMark : Grid
{
    private readonly RotateTransform _rotation = new();
    private readonly bool _running;
    private bool _animating, _subscribed;

    public TaskProgressMark(bool running)
    {
        _running = running; Width = Height = 16; IsHitTestVisible = false;
        var track = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Shape.StrokeProperty, "Line"); Children.Add(track);
        var arc = TaskControls.Stroke(running ? "M 8,1.75 A 6.25,6.25 0 1 1 5.16,13.57" : "M 8,1.75 A 6.25,6.25 0 0 1 14.25,8", running ? "Ink" : "Muted", 1.7);
        arc.RenderTransformOrigin = new Point(.5, .5); arc.RenderTransform = _rotation; Children.Add(arc);
        if (!running) Children.Add(TaskControls.Stroke("M 6.6,5.7 V 10.3 M 9.4,5.7 V 10.3", "Muted", 1.2));
        Loaded += (_, _) =>
        {
            if (!_subscribed) { SystemParameters.StaticPropertyChanged += ParametersChanged; _subscribed = true; }
            UpdateAnimation();
        };
        Unloaded += (_, _) =>
        {
            if (_subscribed) { SystemParameters.StaticPropertyChanged -= ParametersChanged; _subscribed = false; }
            StopAnimation();
        };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }
    private void ParametersChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdateAnimation(); }
    private void UpdateAnimation()
    {
        if (!_running || !IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation) { StopAnimation(); return; }
        if (_animating) return;
        _animating = true;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    private void StopAnimation()
    {
        if (!_animating) return;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, null); _rotation.Angle = 0; _animating = false;
    }
}
