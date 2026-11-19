using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class DockOverlayScrollViewer : DockScrollViewer
{
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private ScrollBar? _rail;
    private bool _revealed;

    public DockOverlayScrollViewer()
    {
        SetResourceReference(TemplateProperty, "DockOverlayScrollViewer");
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_rail is { IsMouseOver: false, IsMouseCaptureWithin: false }) SetRevealed(false);
        };
        Unloaded += (_, _) => ResetRail();
    }

    public override void OnApplyTemplate()
    {
        ResetRail();
        if (_rail != null)
        {
            _rail.MouseEnter -= RailEntered;
            _rail.MouseLeave -= RailLeft;
            _rail.IsMouseCaptureWithinChanged -= RailCaptureChanged;
            _rail.IsVisibleChanged -= RailVisibilityChanged;
        }

        base.OnApplyTemplate();
        _rail = GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;
        if (_rail == null) return;
        _rail.MouseEnter += RailEntered;
        _rail.MouseLeave += RailLeft;
        _rail.IsMouseCaptureWithinChanged += RailCaptureChanged;
        _rail.IsVisibleChanged += RailVisibilityChanged;
        if (_rail.IsMouseOver) Reveal();
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        if (e.VerticalChange != 0) Reveal();
    }

    private void RailEntered(object sender, MouseEventArgs e) => Reveal();
    private void RailLeft(object sender, MouseEventArgs e) => KeepVisibleDuringInteraction();
    private void RailCaptureChanged(object sender, DependencyPropertyChangedEventArgs e) => KeepVisibleDuringInteraction();

    private void RailVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_rail?.IsVisible != true) ResetRail();
        else if (_rail.IsMouseOver) Reveal();
    }

    private void Reveal()
    {
        if (_rail?.IsVisible != true) return;
        SetRevealed(true);
        KeepVisibleDuringInteraction();
    }

    private void KeepVisibleDuringInteraction()
    {
        _idle.Stop();
        if (_rail is not { IsVisible: true } rail) return;
        if (rail.IsMouseOver || rail.IsMouseCaptureWithin) SetRevealed(true);
        else if (_revealed) _idle.Start();
    }

    private void SetRevealed(bool revealed)
    {
        if (_rail == null || _revealed == revealed) return;
        _revealed = revealed;
        double from = _rail.Opacity;
        double to = revealed ? 1 : 0;
        _rail.BeginAnimation(OpacityProperty, null);
        _rail.Opacity = to;
        if (SystemParameters.ClientAreaAnimation)
        {
            var duration = TimeSpan.FromMilliseconds(revealed ? 90 : 180);
            _rail.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, duration) { FillBehavior = FillBehavior.Stop });
        }
    }

    private void ResetRail()
    {
        _idle.Stop();
        _revealed = false;
        if (_rail == null) return;
        _rail.BeginAnimation(OpacityProperty, null);
        _rail.Opacity = 0;
    }
}
