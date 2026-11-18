using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace Levitation.Desktop;

// Keep the live panel inside a transparent envelope spanning both endpoints.
// This avoids clipping the shrinking window or moving between separate HWNDs mid-animation.
internal sealed class DockAnimation(Window window, FrameworkElement surface)
{
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _offset = new();
    private Rect _panel, _entry, _envelope;
    private Forms.Screen? _screen;
    private int _version;
    public bool IsRunning { get; private set; }
    public bool Expanding { get; private set; }

    public bool PointerKeepsPanelOpen(bool docked)
    {
        if (_screen == null || !NativeWindow.TryCursor(out int x, out int y)) return false;
        double scale = NativeWindow.Scale(_screen);
        var area = _screen.WorkingArea;
        return DockLayout.PointerKeepsPanelOpen((x - area.Left) / scale, (y - area.Top) / scale,
            _panel.Left, _panel.Top, _panel.Width, _panel.Height, area.Width / scale, area.Height / scale, docked);
    }

    public void Play(bool expand, Rect entry, Forms.Screen screen, Action<bool> completed)
    {
        if (IsRunning && Expanding == expand) return;
        bool reversing = IsRunning;
        int version = ++_version;
        Expanding = expand;
        if (!reversing)
        {
            _screen = screen;
            var position = NativeWindow.RelativePosition(window, screen);
            _panel = new Rect(position.Left, position.Top, window.Width, window.Height);
            _entry = entry; _envelope = Rect.Union(_panel, _entry);
            surface.Width = _panel.Width; surface.Height = _panel.Height;
            surface.HorizontalAlignment = HorizontalAlignment.Left; surface.VerticalAlignment = VerticalAlignment.Top;
            var transform = new TransformGroup(); transform.Children.Add(_scale); transform.Children.Add(_offset);
            surface.RenderTransform = transform;
            IsRunning = true;
            NativeWindow.PlaceBounds(window, screen, _envelope);
            _scale.ScaleX = expand ? _entry.Width / _panel.Width : 1;
            _scale.ScaleY = expand ? _entry.Height / _panel.Height : 1;
            _offset.X = (expand ? _entry.Left : _panel.Left) - _envelope.Left;
            _offset.Y = (expand ? _entry.Top : _panel.Top) - _envelope.Top;
            surface.Opacity = expand ? 0 : 1;
        }
        if (!SystemParameters.ClientAreaAnimation) { Finish(expand); completed(expand); return; }
        var duration = TimeSpan.FromMilliseconds(200);
        Animate(_scale, ScaleTransform.ScaleXProperty, expand ? 1 : _entry.Width / _panel.Width, duration);
        Animate(_scale, ScaleTransform.ScaleYProperty, expand ? 1 : _entry.Height / _panel.Height, duration);
        Animate(_offset, TranslateTransform.XProperty, (expand ? _panel.Left : _entry.Left) - _envelope.Left, duration);
        Animate(_offset, TranslateTransform.YProperty, (expand ? _panel.Top : _entry.Top) - _envelope.Top, duration);
        var fade = Animation(surface.Opacity, expand ? 1 : 0, duration);
        fade.Completed += (_, _) =>
        {
            if (version != _version || !IsRunning) return;
            Finish(expand); completed(expand);
        };
        surface.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static DoubleAnimation Animation(double from, double to, TimeSpan duration) => new(from, to, duration)
    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
    private static void Animate(Animatable target, DependencyProperty property, double value, TimeSpan duration) =>
        target.BeginAnimation(property, Animation((double)target.GetValue(property), value, duration));

    public void Cancel(bool expanded)
    {
        if (!IsRunning) return;
        ++_version; Finish(expanded);
    }

    private void Finish(bool expanded)
    {
        if (!expanded && window.IsVisible) window.Hide();
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _offset.BeginAnimation(TranslateTransform.XProperty, null); _offset.BeginAnimation(TranslateTransform.YProperty, null);
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.RenderTransform = Transform.Identity; surface.Width = double.NaN; surface.Height = double.NaN;
        surface.HorizontalAlignment = HorizontalAlignment.Stretch; surface.VerticalAlignment = VerticalAlignment.Stretch; surface.Opacity = 1;
        IsRunning = false;
        if (_screen != null && PresentationSource.FromVisual(window) != null) NativeWindow.PlaceBounds(window, _screen, _panel);
    }
}
