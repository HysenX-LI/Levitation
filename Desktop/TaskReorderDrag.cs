using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed record TaskTreeRow(Guid Id, int Depth, Grid Group, Grid Header, Border Body, StackPanel Children, ScrollViewer Viewport);

// A captured ancestor survives visual reparenting. The model changes only after
// mouse-up; rebuilding it also restores the original UI on cancellation.
internal sealed class TaskReorderDrag
{
    private readonly DockScrollViewer _viewport;
    private readonly TaskTreeRow _source;
    private readonly TaskTreeRow[] _rows;
    private readonly DockScrollViewer[] _scrollAreas;
    private readonly Func<TaskMove, bool> _canDrop;
    private readonly Action<TaskTreeRow> _expand;
    private readonly Action<TaskMove?> _finished;
    private readonly HashSet<Guid> _dragged;
    private readonly TaskRowMotion _motion;
    private readonly Func<Guid, TaskTreeRow?> _locate;
    private Point? _anchor;
    private TaskMove? _anchorMove;
    private readonly DispatcherTimer _scroll = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly AdornerLayer _layer;
    private readonly DragVisual _ghost;
    private readonly Point _grab;
    private readonly Cursor? _previousCursor;
    private TaskMove? _move, _preview;
    private DateTimeOffset _hoverSince;
    private bool _ending, _done;

    public TaskReorderDrag(DockScrollViewer viewport, TaskTreeRow source, TaskTreeRow[] rows, DockScrollViewer[] scrollAreas, Point grab,
        Func<TaskMove, bool> canDrop, Action<TaskTreeRow> expand, Action<TaskMove?> finished, TaskRowMotion motion, Func<Guid, TaskTreeRow?> locate)
    {
        _viewport = viewport; _source = source; _rows = rows; _scrollAreas = scrollAreas; _canDrop = canDrop; _expand = expand; _finished = finished; _motion = motion; _locate = locate;
        _grab = source.Group.TranslatePoint(grab, source.Header); _previousCursor = Mouse.OverrideCursor;
        _dragged = rows.Where(r => r == source || source.Group.IsAncestorOf(r.Group)).Select(r => r.Id).ToHashSet();
        _layer = AdornerLayer.GetAdornerLayer(viewport) ?? throw new InvalidOperationException("排序视图尚未准备好。");
        var dpi = VisualTreeHelper.GetDpi(source.Header);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(source.Header.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(source.Header.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(source.Header), null, new Rect(0, 0, source.Header.ActualWidth, source.Header.ActualHeight));
        bitmap.Render(drawing); bitmap.Freeze();
        _ghost = new DragVisual(viewport, bitmap, source.Header.ActualWidth, source.Header.ActualHeight);
        var start = source.Header.TranslatePoint(new Point(), viewport); _ghost.Left = start.X; _ghost.Top = start.Y;
    }
    public bool Begin()
    {
        if (!_viewport.CaptureMouse()) return false;
        var previous = CapturePositions(); _motion.Remove(_source.Header);
        _source.Body.BeginAnimation(FrameworkElement.HeightProperty, null); _source.Body.Visibility = Visibility.Collapsed;
        _source.Group.Opacity = 0; _layer.Add(_ghost); _viewport.UpdateLayout();
        AnimateRows(previous);
        _viewport.PreviewMouseMove += Move; _viewport.PreviewMouseLeftButtonUp += Up;
        _viewport.LostMouseCapture += LostCapture; _viewport.PreviewMouseWheel += Wheel;
        _scroll.Tick += Scroll; _scroll.Start(); Update(); return true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (_ending) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Update(true); End(true); return; }
        Update(); e.Handled = true;
    }
    private void Up(object sender, MouseButtonEventArgs e) { if (!_ending) { Update(true); End(true); } e.Handled = true; }
    private void LostCapture(object sender, MouseEventArgs e) { if (!_ending && !_viewport.IsMouseCaptured) End(false); }
    private DockScrollViewer ScrollArea(Point point) => _scrollAreas.FirstOrDefault(area => area.IsVisible && Bounds(area).Contains(point)) ?? _viewport;
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (_ending) return;
        var area = ScrollArea(e.GetPosition(_viewport));
        if (area.TryScrollWheel(e.Delta) || (area != _viewport && _viewport.TryScrollWheel(e.Delta)))
        {
            _viewport.UpdateLayout();
            Update();
        }
        e.Handled = true;
    }
    private void Scroll(object? sender, EventArgs e)
    {
        if (_ending) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { Update(true); End(true); return; }
        var point = Mouse.GetPosition(_viewport); var area = ScrollArea(point); var local = Mouse.GetPosition(area);
        double speed = local.Y < 26 ? -Math.Clamp((26 - local.Y) / 4, 1, 7) : local.Y > area.ActualHeight - 26 ? Math.Clamp((local.Y - area.ActualHeight + 26) / 4, 1, 7) : 0;
        if (speed != 0) { area.ScrollToVerticalOffset(area.VerticalOffset + speed); _viewport.UpdateLayout(); }
        Update();
    }
    private Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), _viewport), element.RenderSize);
    private Rect HeaderBounds(TaskTreeRow row)
    {
        var bounds = Bounds(row.Header);
        // FLIP transforms are presentation only, never part of drop hit testing.
        if (row.Header.RenderTransform is TranslateTransform transform) bounds.Y -= transform.Y;
        return bounds;
    }
    private Rect VisibleBounds(TaskTreeRow row)
    {
        if (!row.Header.IsVisible || _dragged.Contains(row.Id)) return Rect.Empty;
        var bounds = HeaderBounds(row); bounds.Intersect(Bounds(row.Viewport)); bounds.Intersect(new Rect(_viewport.RenderSize)); return bounds;
    }
    private void Update(bool force = false)
    {
        if (_ending) return;
        var point = Mouse.GetPosition(_viewport);
        _ghost.Left = Math.Clamp(point.X - _grab.X, 0, Math.Max(0, _viewport.ActualWidth - _ghost.WidthOfRow));
        _ghost.Top = Math.Clamp(point.Y - _grab.Y, -16, Math.Max(0, _viewport.ActualHeight - 16));
        TaskTreeRow? target = null; TaskDropPosition position = TaskDropPosition.Before;
        if (new Rect(_viewport.RenderSize).Contains(point))
        {
            var visible = _rows.Select(row => (Row: row, Rect: VisibleBounds(row))).Where(item => !item.Rect.IsEmpty && item.Rect.Height > 4).ToArray();
            var hit = visible.FirstOrDefault(item => point.Y >= item.Rect.Top && point.Y <= item.Rect.Bottom && Bounds(item.Row.Viewport).Contains(point));
            if (hit.Row != null)
            {
                target = hit.Row; var rect = HeaderBounds(target); double fraction = (point.Y - rect.Top) / rect.Height;
                position = fraction < .28 ? TaskDropPosition.Before : fraction > .72 ? TaskDropPosition.After : TaskDropPosition.Inside;
                if (_move?.TargetId == target.Id)
                {
                    if (_move.Position == TaskDropPosition.Inside && fraction > .21 && fraction < .79) position = TaskDropPosition.Inside;
                    else if (_move.Position == TaskDropPosition.Before && fraction < .35) position = TaskDropPosition.Before;
                    else if (_move.Position == TaskDropPosition.After && fraction > .65) position = TaskDropPosition.After;
                }
            }
            else if (_move != null && Bounds(_source.Group).Contains(point))
            {
                target = _rows.FirstOrDefault(row => row.Id == _move.TargetId); position = _move.Position;
            }
            else
            {
                var last = visible.Where(item => Bounds(item.Row.Viewport).Contains(point) && point.Y > item.Rect.Bottom).OrderByDescending(item => item.Rect.Bottom).FirstOrDefault();
                if (last.Row != null && point.Y - last.Rect.Bottom < 36) { target = last.Row; position = TaskDropPosition.After; }
            }
        }
        if (_anchor is Point anchor && (point - anchor).Length < 6 && _anchorMove != null)
        {
            var locked = _rows.FirstOrDefault(row => row.Id == _anchorMove.TargetId);
            if (locked != null && !VisibleBounds(locked).IsEmpty) { target = locked; position = _anchorMove.Position; }
        }
        else { _anchor = null; _anchorMove = null; }
        var candidate = target == null ? null : new TaskMove(_source.Id, target.Id, position);
        if (candidate == null || !_canDrop(candidate))
        {
            _move = null; _preview = null; _ghost.Hint = null; Mouse.OverrideCursor = Cursors.No; _ghost.InvalidateVisual(); return;
        }
        Mouse.OverrideCursor = Cursors.Arrow;
        if (_move != candidate) { _move = candidate; _hoverSince = DateTimeOffset.UtcNow; }
        bool needsExpansion = position == TaskDropPosition.Inside && target!.Children.Children.Count > 0 && target.Body.Visibility != Visibility.Visible;
        if (_preview != candidate && (force || !needsExpansion || (DateTimeOffset.UtcNow - _hoverSince).TotalMilliseconds >= 160))
        { Preview(target!, position); _preview = candidate; }
        var hint = HeaderBounds(target!);
        if (position == TaskDropPosition.After) hint.Y = Bounds(target!.Group).Bottom;
        _ghost.Hint = hint; _ghost.Inside = position == TaskDropPosition.Inside; _ghost.InvalidateVisual();
    }
    private void Preview(TaskTreeRow target, TaskDropPosition position)
    {
        var previous = CapturePositions();
        _anchor = Mouse.GetPosition(_viewport); _anchorMove = new TaskMove(_source.Id, target.Id, position);
        var destination = position == TaskDropPosition.Inside ? target.Children : (StackPanel)target.Group.Parent;
        if (position == TaskDropPosition.Inside)
        {
            _expand(target); target.Body.BeginAnimation(FrameworkElement.HeightProperty, null); target.Body.Height = double.NaN; target.Body.Visibility = Visibility.Visible;
        }
        ((Panel)_source.Group.Parent).Children.Remove(_source.Group);
        int index = position == TaskDropPosition.Inside ? destination.Children.Count : destination.Children.IndexOf(target.Group) + (position == TaskDropPosition.After ? 1 : 0);
        destination.Children.Insert(index, _source.Group); _viewport.UpdateLayout();
        AnimateRows(previous);
    }
    private Dictionary<Guid, double> CapturePositions() => _rows.Where(row => !_dragged.Contains(row.Id) && row.Header.IsVisible).ToDictionary(row => row.Id, row => Bounds(row.Header).Y);
    private void AnimateRows(Dictionary<Guid, double> previous) => _motion.Animate(_rows
        .Where(row => !_dragged.Contains(row.Id) && row.Header.IsVisible && previous.ContainsKey(row.Id))
        .Select(row => (row.Header, previous[row.Id])));
    public void Cancel() => End(false);
    private void End(bool commit)
    {
        if (_done) return; _done = _ending = true; _scroll.Stop(); _scroll.Tick -= Scroll;
        _viewport.PreviewMouseMove -= Move; _viewport.PreviewMouseLeftButtonUp -= Up; _viewport.LostMouseCapture -= LostCapture; _viewport.PreviewMouseWheel -= Wheel;
        if (_viewport.IsMouseCaptured) _viewport.ReleaseMouseCapture(); Mouse.OverrideCursor = _previousCursor;
        commit = commit && _move != null && _canDrop(_move); _ghost.Hint = null;
        _source.Group.Opacity = 1;
        if (!commit) _motion.Stop();
        var previous = CapturePositions();
        _finished(commit ? _move : null); // Save immediately; the landing is presentation only.
        _viewport.UpdateLayout();
        _motion.Animate(previous.Where(pair => _locate(pair.Key)?.Header.IsVisible == true)
            .Select(pair => (_locate(pair.Key)!.Header, pair.Value)));
        var target = _locate(_source.Id)?.Header;
        if (target == null || !target.IsVisible || !SystemParameters.ClientAreaAnimation) { _layer.Remove(_ghost); return; }
        var destination = target.TranslatePoint(new Point(), _viewport);
        target.Opacity = 0; bool removed = false;
        void Clear()
        {
            if (removed) return; removed = true;
            _viewport.PreviewMouseDown -= NextInput; _viewport.PreviewKeyDown -= NextKey;
            target.Opacity = 1; _ghost.BeginAnimation(DragVisual.LeftProperty, null); _ghost.BeginAnimation(DragVisual.TopProperty, null); _layer.Remove(_ghost);
        }
        void NextInput(object sender, MouseButtonEventArgs e) => Clear();
        void NextKey(object sender, KeyEventArgs e) => Clear();
        _viewport.PreviewMouseDown += NextInput; _viewport.PreviewKeyDown += NextKey;
        _ghost.BeginAnimation(DragVisual.LeftProperty, new DoubleAnimation(_ghost.Left, destination.X, TimeSpan.FromMilliseconds(85)));
        var settle = new DoubleAnimation(_ghost.Top, destination.Y, TimeSpan.FromMilliseconds(85)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        settle.Completed += (_, _) => Clear(); _ghost.BeginAnimation(DragVisual.TopProperty, settle);
    }
    private sealed class DragVisual : Adorner
    {
        public static readonly DependencyProperty TopProperty = DependencyProperty.Register(nameof(Top), typeof(double), typeof(DragVisual), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LeftProperty = DependencyProperty.Register(nameof(Left), typeof(double), typeof(DragVisual), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Top { get => (double)GetValue(TopProperty); set => SetValue(TopProperty, value); }
        public double Left { get => (double)GetValue(LeftProperty); set => SetValue(LeftProperty, value); }
        public double WidthOfRow { get; }
        public Rect? Hint { get; set; }
        public bool Inside { get; set; }
        private readonly ImageSource _image; private readonly double _height;
        public DragVisual(UIElement owner, ImageSource image, double width, double height) : base(owner)
        {
            _image = image; WidthOfRow = width; _height = height; IsHitTestVisible = false;
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = .16 };
        }
        protected override void OnRender(DrawingContext context)
        {
            context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
            var pen = new Pen(Ui.Brush("Accent"), 1.5);
            if (Hint is Rect hint)
            {
                if (Inside) context.DrawRoundedRectangle(Ui.Brush("Soft"), pen, hint, 6, 6);
                else { context.DrawLine(pen, hint.TopLeft, hint.TopRight); context.DrawEllipse(Ui.Brush("Accent"), null, hint.TopLeft, 2, 2); }
            }
            context.PushOpacity(.86); var bounds = new Rect(Left, Top, WidthOfRow, _height);
            context.DrawRoundedRectangle(Ui.Brush("TaskHoverSurface"), null, bounds, 6, 6); context.DrawImage(_image, bounds); context.Pop(); context.Pop();
        }
    }
}
