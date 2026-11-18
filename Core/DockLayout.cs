namespace Levitation.Core;

public static class DockLayout
{
    public const double TaskSectionMinimum = 240, CardSectionMinimum = 188;
    private const double WindowMargin = 12, MinimumWidth = 280, MaximumWidth = 640, MinimumHeight = 280;
    public static double SectionHeight(bool tasks, double height) => Math.Clamp(
        double.IsFinite(height) ? height : tasks ? 320 : 220, tasks ? TaskSectionMinimum : CardSectionMinimum, 1200);
    public static double Normalize(double position) => double.IsFinite(position) ? Math.Clamp(position, 0, 1) : .5;
    public static bool ShouldSnap(double horizontalTravel, double verticalTravel, double panelRight, double workWidth) =>
        horizontalTravel >= 56 && horizontalTravel >= Math.Abs(verticalTravel) * 1.25 && panelRight >= workWidth + 20;

    public static double PanelWidth(double workWidth, double preferredWidth)
    {
        double maximum = Math.Min(MaximumWidth, Math.Max(1, workWidth - 2 * WindowMargin));
        return Math.Clamp(double.IsFinite(preferredWidth) ? preferredWidth : 340, Math.Min(MinimumWidth, maximum), maximum);
    }

    public static double PanelHeight(double workHeight, double? preferredHeight)
    {
        double maximum = Math.Max(1, workHeight - 24);
        double minimum = Math.Min(MinimumHeight, maximum);
        double requested = preferredHeight is double height && double.IsFinite(height) ? height : Math.Min(860, workHeight * .78);
        return Math.Clamp(requested, minimum, maximum);
    }

    public static (double Left, double Top, double Width, double Height) ResizePanel(
        double left, double top, double width, double height, double workWidth, double workHeight, double? dx, double? dy)
    {
        double right = left + width;
        if (dx.HasValue)
        {
            // Keep the right edge anchored, including when the window is floating.
            double maximum = Math.Max(width, Math.Min(PanelWidth(workWidth, MaximumWidth), right - Math.Min(WindowMargin, left)));
            width = Math.Clamp(width - dx.Value, Math.Min(MinimumWidth, maximum), maximum);
        }
        if (dy.HasValue)
        {
            double maximum = Math.Max(height, workHeight - top - WindowMargin);
            height = Math.Clamp(height + dy.Value, Math.Min(MinimumHeight, maximum), maximum);
        }
        return (right - width, top, width, height);
    }

    public static bool CanAutoHide(bool pointerInside, bool pinned, bool interacting, bool childOpen, bool menuOpen, bool typing) =>
        !pointerInside && !pinned && !interacting && !childOpen && !menuOpen && !typing;

    // Use logical coordinates within one monitor's working area. This is only
    // an auto-hide tolerance, not an enlarged window or mouse interception area.
    public static bool PointerKeepsPanelOpen(double x, double y, double left, double top, double width, double height,
        double workWidth, double workHeight, bool docked)
    {
        if (x < 0 || x >= workWidth || y < 0 || y >= workHeight) return false;
        double right = docked ? workWidth : Math.Min(workWidth, left + width + 24);
        return x >= Math.Max(0, left - 8) && x < right &&
            y >= Math.Max(0, top - 16) && y < Math.Min(workHeight, top + height + 16);
    }

    // Coordinates are relative to the monitor's working area and use the same units.
    // The expanded window follows the entry's centre, clamped inside the working area.
    public static (double EdgeTop, double PanelTop) Calculate(double workHeight, double panelHeight, double edgeHeight, double position, double margin = 12)
    {
        double edgeTop = FloatingOffset(workHeight, edgeHeight, position, margin);
        double panelMargin = Math.Min(margin, Math.Max(0, workHeight - panelHeight) / 2);
        double panelTop = Math.Clamp(edgeTop + edgeHeight / 2 - panelHeight / 2, panelMargin, Math.Max(panelMargin, workHeight - panelHeight - panelMargin));
        return (edgeTop, panelTop);
    }

    public static double FloatingOffset(double workExtent, double extent, double position, double margin = 12)
    {
        double inset = Math.Min(margin, Math.Max(0, workExtent - extent) / 2);
        return inset + Normalize(position) * Math.Max(0, workExtent - extent - 2 * inset);
    }

    public static double PositionFromEdgeTop(double workHeight, double edgeHeight, double top, double margin = 12)
    {
        double edgeMargin = Math.Min(margin, Math.Max(0, workHeight - edgeHeight) / 2);
        double travel = Math.Max(0, workHeight - edgeHeight - 2 * edgeMargin);
        return travel > 0 ? Normalize((top - edgeMargin) / travel) : .5;
    }
}
