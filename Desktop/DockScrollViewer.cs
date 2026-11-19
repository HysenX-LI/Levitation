namespace Levitation.Desktop;

internal class DockScrollViewer : ScrollViewer
{
    private const double DistancePerWheelNotch = 48;
    private const int WheelDeltaPerNotch = 120;
    private double? _requestedOffset;

    public DockScrollViewer()
    {
        SetResourceReference(StyleProperty, typeof(ScrollViewer));
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CanContentScroll = false;
        Unloaded += ClearRequestedOffset;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Leave the event available to the parent when we cannot scroll in this direction.
        if (!e.Handled) e.Handled = TryScrollWheel(e.Delta);
    }

    internal bool TryScrollWheel(int delta)
    {
        if (delta == 0 || ScrollInfo is not { CanVerticallyScroll: true } info) return false;

        double maximum = Math.Max(0, info.ExtentHeight - info.ViewportHeight);
        double from = Math.Clamp(_requestedOffset ?? info.VerticalOffset, 0, maximum);
        double to = Math.Clamp(from - delta * DistancePerWheelNotch / WheelDeltaPerNotch, 0, maximum);
        if (from == to) return false;

        // Several wheel events can arrive before layout publishes the next offset.
        if (!_requestedOffset.HasValue) LayoutUpdated += ClearRequestedOffset;
        _requestedOffset = to;
        info.SetVerticalOffset(to);
        return true;
    }

    private void ClearRequestedOffset(object? sender, EventArgs e)
    {
        _requestedOffset = null;
        LayoutUpdated -= ClearRequestedOffset;
    }
}
