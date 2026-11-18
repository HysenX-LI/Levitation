using System.Diagnostics;

namespace Levitation.Desktop;

// A single rendering loop retargets critically damped movement without restarting
// an easing curve each time the pointer crosses a row.
internal sealed class TaskRowMotion(FrameworkElement viewport)
{
    private sealed class Motion { public double Y, Velocity; public TranslateTransform Transform = new(); }
    private readonly Dictionary<Grid, Motion> _rows = new();
    private long _last;
    private bool _listening;
    public void Animate(IEnumerable<(Grid Row, double Before)> positions)
    {
        if (!SystemParameters.ClientAreaAnimation) { Stop(); return; }
        foreach (var (row, before) in positions)
        {
            double current = row.RenderTransform is TranslateTransform existing ? existing.Y : 0;
            double offset = before - (row.TranslatePoint(new Point(), viewport).Y - current);
            if (!_rows.TryGetValue(row, out var motion)) motion = new Motion();
            motion.Y = offset; motion.Transform.Y = offset; row.RenderTransform = motion.Transform; _rows[row] = motion;
        }
        if (_listening || _rows.Count == 0) return;
        _last = Stopwatch.GetTimestamp(); _listening = true; CompositionTarget.Rendering += Tick;
    }
    private void Tick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp(); double dt = Math.Min(.032, (now - _last) / (double)Stopwatch.Frequency); _last = now;
        if (!SystemParameters.ClientAreaAnimation) { Stop(); return; }
        foreach (var (row, motion) in _rows.ToArray())
        {
            if (!row.IsLoaded || !row.IsVisible) { Remove(row); continue; }
            const double omega = 32; double a = motion.Velocity + omega * motion.Y, decay = Math.Exp(-omega * dt);
            motion.Y = (motion.Y + a * dt) * decay; motion.Velocity = (motion.Velocity - omega * a * dt) * decay;
            if (Math.Abs(motion.Y) < .15 && Math.Abs(motion.Velocity) < 2) { Remove(row); continue; }
            motion.Transform.Y = motion.Y;
        }
        if (_rows.Count == 0) Stop();
    }
    public void Remove(Grid row) { _rows.Remove(row); row.RenderTransform = Transform.Identity; }
    public void Stop()
    {
        foreach (var row in _rows.Keys) row.RenderTransform = Transform.Identity; _rows.Clear();
        if (_listening) CompositionTarget.Rendering -= Tick; _listening = false;
    }
}
