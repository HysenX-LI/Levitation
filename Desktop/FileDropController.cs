using System.Globalization;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class FileDropController : IDisposable
{
    private readonly Window _window, _edge;
    private readonly FrameworkElement _cards;
    private readonly Action _expand;
    private readonly Action<IEnumerable<(string Path, bool IsFolder)>> _add;
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private DateTimeOffset _lastOver;
    private FileDropHint? _hint;
    private AdornerLayer? _layer;
    public bool IsActive => (DateTimeOffset.UtcNow - _lastOver).TotalSeconds < 1;
    public FileDropController(Window window, Window edge, FrameworkElement cards, Action expand, Action<IEnumerable<(string Path, bool IsFolder)>> add)
    {
        _window = window; _edge = edge; _cards = cards; _expand = expand; _add = add;
        window.AllowDrop = edge.AllowDrop = true;
        window.PreviewDragEnter += Over; window.PreviewDragOver += Over; window.PreviewDragLeave += Leave; window.PreviewDrop += Drop;
        edge.PreviewDragEnter += EdgeOver; edge.PreviewDragOver += EdgeOver; edge.PreviewDragLeave += EdgeLeave; edge.PreviewDrop += Drop;
        _hover.Tick += (_, _) => { _hover.Stop(); if (IsActive) _expand(); };
    }
    private static string[] Paths(DragEventArgs e)
    {
        try { return e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] ?? [] : []; }
        catch (System.Runtime.InteropServices.COMException) { return []; }
    }
    private static DragDropEffects Effect(DragEventArgs e) => (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
    private bool OnCards(DragEventArgs e) => _cards.IsVisible && new Rect(_cards.RenderSize).Contains(e.GetPosition(_cards));
    private void EdgeOver(object sender, DragEventArgs e)
    {
        if (Paths(e).Length == 0) return;
        _lastOver = DateTimeOffset.UtcNow; e.Effects = Effect(e); e.Handled = true;
        if (!_hover.IsEnabled) _hover.Start();
    }
    private void Over(object sender, DragEventArgs e)
    {
        var paths = Paths(e); if (paths.Length == 0) return;
        _lastOver = DateTimeOffset.UtcNow; e.Effects = OnCards(e) ? Effect(e) : DragDropEffects.None; e.Handled = true;
        if (e.Effects == DragDropEffects.None) { HideHint(); return; }
        _layer ??= AdornerLayer.GetAdornerLayer(_cards);
        if (_hint == null && _layer != null) { _hint = new FileDropHint(_cards); _layer.Add(_hint); }
        if (_hint != null) { _hint.Count = paths.Length; _hint.InvalidateVisual(); }
    }
    private void Leave(object sender, DragEventArgs e)
    {
        if (new Rect(_window.RenderSize).Contains(e.GetPosition(_window))) return;
        _lastOver = DateTimeOffset.MinValue; HideHint();
    }
    private void EdgeLeave(object sender, DragEventArgs e) { _hover.Stop(); }
    private void Drop(object sender, DragEventArgs e)
    {
        var paths = Paths(e); if (paths.Length == 0) return;
        bool accept = sender == _edge || OnCards(e); _hover.Stop(); HideHint(); _lastOver = DateTimeOffset.MinValue;
        e.Effects = accept ? Effect(e) : DragDropEffects.None; e.Handled = true;
        if (e.Effects == DragDropEffects.None) return;
        App.Try(() =>
        {
            var files = paths.Select(path => Path.GetFullPath(path)).Select(path => (Path: path, IsFolder: Directory.Exists(path))).ToArray();
            if (files.Any(file => !file.IsFolder && !File.Exists(file.Path))) throw new IOException("有文件已移动、删除或尚未保存到本地，请从文件夹中重新拖入。");
            _add(files); _expand();
        });
    }
    private void HideHint() { if (_hint != null) { _layer?.Remove(_hint); _hint = null; } }
    public void Dispose()
    {
        _hover.Stop(); HideHint();
        _window.PreviewDragEnter -= Over; _window.PreviewDragOver -= Over; _window.PreviewDragLeave -= Leave; _window.PreviewDrop -= Drop;
        _edge.PreviewDragEnter -= EdgeOver; _edge.PreviewDragOver -= EdgeOver; _edge.PreviewDragLeave -= EdgeLeave; _edge.PreviewDrop -= Drop;
    }
    private sealed class FileDropHint : Adorner
    {
        public FileDropHint(UIElement owner) : base(owner) { IsHitTestVisible = false; }
        public int Count { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            var bounds = new Rect(2, 2, Math.Max(0, RenderSize.Width - 4), Math.Max(0, RenderSize.Height - 4));
            var fill = Ui.Brush("Paper").CloneCurrentValue(); fill.Opacity = .94;
            context.DrawRoundedRectangle(fill, new Pen(Ui.Brush("ControlLine"), 1) { DashStyle = DashStyles.Dash }, bounds, 10, 10);
            var text = new FormattedText($"松开以添加 {Count} 个文件入口", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Ui.TaskFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, Ui.Brush("Ink"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(text, new Point(Math.Max(8, (RenderSize.Width - text.Width) / 2), Math.Max(6, (RenderSize.Height - text.Height) / 2)));
        }
    }
}
