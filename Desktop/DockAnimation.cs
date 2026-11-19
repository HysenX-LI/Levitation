using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace Levitation.Desktop;

// Animate within the selected monitor's window bounds. The slide is clipped at
// the right edge of the working area, including when another monitor is adjacent.
internal sealed class DockAnimation(Window window, Window edgeWindow, FrameworkElement surface)
{
    private const double EdgeOpacity = .72;
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _offset = new();
    private readonly BitmapCache _bitmapCache = new();
    private Rect _panel, _entry, _envelope;
    private Forms.Screen? _screen;
    private CacheMode? _previousCache;
    private bool _slide;
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

    public void Play(bool expand, Rect entry, Forms.Screen screen, DockTransition transition, Action<bool> completed)
    {
        edgeWindow.Opacity = expand ? EdgeOpacity : 0;
        if (!SystemParameters.ClientAreaAnimation)
        {
            if (!expand) PlaceEntry(screen, entry);
            SettleVisibility(expand);
            completed(expand);
            return;
        }

        _screen = screen;
        var position = NativeWindow.RelativePosition(window, screen);
        _panel = new Rect(position.Left, position.Top, window.Width, window.Height);
        _entry = entry;
        _slide = transition == DockTransition.Slide;
        _envelope = Rect.Union(_panel, _entry);
        surface.Width = _panel.Width;
        surface.Height = _panel.Height;
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        var transform = new TransformGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(_offset);
        surface.RenderTransform = transform;
        _previousCache = surface.CacheMode;
        if (!_slide) surface.CacheMode = _bitmapCache;
        SetPanelPose(expand ? 0 : 1);
        IsRunning = true;
        NativeWindow.PlaceBounds(window, screen, _envelope);
        PlaceEntry(screen, entry);
        Begin(expand, expand ? 0 : 1, completed);
    }

    private void PlaceEntry(Forms.Screen screen, Rect entry)
    {
        if (!edgeWindow.IsVisible) edgeWindow.Show();
        // Bring the entry above the panel so the sliding surface cannot hide its fade-in.
        NativeWindow.PlaceBounds(edgeWindow, screen, entry);
    }

    public void Reverse(bool expand, Action<bool> completed)
    {
        // Sample the rendered pose before replacing the current clocks.
        double progress = _slide
            ? (_entry.Right - _envelope.Left - _offset.X) / (_entry.Right - _panel.Left)
            : surface.Opacity;
        Begin(expand, Math.Clamp(progress, 0, 1), completed);
    }

    private double ClosedScaleX => _slide ? 1 : _entry.Width / _panel.Width;
    private double ClosedScaleY => _slide ? 1 : _entry.Height / _panel.Height;
    private double ClosedLeft => (_slide ? _entry.Right : _entry.Left) - _envelope.Left;
    private double ClosedTop => (_slide ? _panel.Top : _entry.Top) - _envelope.Top;
    private double OpenLeft => _panel.Left - _envelope.Left;
    private double OpenTop => _panel.Top - _envelope.Top;

    private void SetPanelPose(double progress)
    {
        _scale.ScaleX = ClosedScaleX + (1 - ClosedScaleX) * progress;
        _scale.ScaleY = ClosedScaleY + (1 - ClosedScaleY) * progress;
        _offset.X = ClosedLeft + (OpenLeft - ClosedLeft) * progress;
        _offset.Y = ClosedTop + (OpenTop - ClosedTop) * progress;
        surface.Opacity = _slide ? 1 : progress;
    }

    private void Begin(bool expand, double progress, Action<bool> completed)
    {
        Expanding = expand;
        int version = ++_version;
        double remaining = expand ? 1 - progress : progress;
        if (!SystemParameters.ClientAreaAnimation || remaining < .001)
        {
            Finish(expand);
            completed(expand);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(Math.Clamp(220 * remaining, 35, 220));
        var easing = new QuadraticEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn };
        var timeline = new ParallelTimeline { Duration = duration };
        var targets = new List<(IAnimatable Target, DependencyProperty Property)>();
        void Add(IAnimatable target, DependencyProperty property, AnimationTimeline animation)
        {
            targets.Add((target, property));
            timeline.Children.Add(animation);
        }
        DoubleAnimation Move(double from, double to) => new(from, to, duration) { EasingFunction = easing };

        Add(_offset, TranslateTransform.XProperty, Move(_offset.X, expand ? OpenLeft : ClosedLeft));
        if (!_slide)
        {
            Add(_scale, ScaleTransform.ScaleXProperty, Move(_scale.ScaleX, expand ? 1 : ClosedScaleX));
            Add(_scale, ScaleTransform.ScaleYProperty, Move(_scale.ScaleY, expand ? 1 : ClosedScaleY));
            Add(_offset, TranslateTransform.YProperty, Move(_offset.Y, expand ? OpenTop : ClosedTop));
            Add(surface, UIElement.OpacityProperty, Move(surface.Opacity, expand ? 1 : 0));
        }

        // Reveal the real entry only during the last part of the return trip.
        // On reversal, use its current opacity so neither HWND flashes.
        var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(edgeWindow.Opacity, KeyTime.FromPercent(0)));
        if (expand)
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(Math.Min(1, .40 / remaining))));
        else
        {
            // The panel accelerates on exit, so convert the reveal position to eased time.
            double revealAt = Math.Sqrt(Math.Max(0, 1 - .42 / remaining));
            if (revealAt > 0)
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(edgeWindow.Opacity, KeyTime.FromPercent(revealAt)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(EdgeOpacity, KeyTime.FromPercent(1)));
        }
        Add(edgeWindow, UIElement.OpacityProperty, fade);

        var clock = (ClockGroup)timeline.CreateClock();
        clock.Completed += (_, _) =>
        {
            if (version != _version || !IsRunning) return;
            Finish(expand);
            completed(expand);
        };
        for (int i = 0; i < targets.Count; i++)
        {
            var (target, property) = targets[i];
            target.ApplyAnimationClock(property, (AnimationClock)clock.Children[i]);
        }
    }

    public void Cancel(bool expanded)
    {
        if (!IsRunning) return;
        ++_version;
        Finish(expanded);
    }

    private void Finish(bool expanded)
    {
        SettleVisibility(expanded);
        if (!_slide)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _offset.BeginAnimation(TranslateTransform.YProperty, null);
        }
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        surface.CacheMode = _previousCache;
        surface.RenderTransform = Transform.Identity;
        surface.Width = surface.Height = double.NaN;
        surface.HorizontalAlignment = HorizontalAlignment.Stretch;
        surface.VerticalAlignment = VerticalAlignment.Stretch;
        IsRunning = false;
        if (_screen != null && PresentationSource.FromVisual(window) != null)
            NativeWindow.PlaceBounds(window, _screen, _panel);
    }

    private void SettleVisibility(bool expanded)
    {
        if (expanded) edgeWindow.Hide();
        else window.Hide();
        edgeWindow.BeginAnimation(UIElement.OpacityProperty, null);
        edgeWindow.Opacity = EdgeOpacity;
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.Opacity = 1;
    }
}
