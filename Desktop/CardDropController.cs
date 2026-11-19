using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DragData = System.Windows.IDataObject;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class CardDropController : IDisposable
{
    private const string UnicodeUrl = "UniformResourceLocatorW", AnsiUrl = "UniformResourceLocator";
    private const int MaxTextBytes = 1024 * 1024;
    private enum Kind { None, Files, Link, Text }

    private readonly Window _window, _edge;
    private readonly FrameworkElement _cards;
    private readonly Action _expand;
    private readonly Action<IEnumerable<(string Path, bool IsFolder)>> _addFiles;
    private readonly Action<string> _addText;
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private DateTimeOffset _lastOver;
    private DropHint? _hint;
    private AdornerLayer? _layer;
    public bool IsActive => (DateTimeOffset.UtcNow - _lastOver).TotalSeconds < 1;
    public CardDropController(Window window, Window edge, FrameworkElement cards, Action expand, Action<IEnumerable<(string Path, bool IsFolder)>> addFiles, Action<string> addText)
    {
        _window = window; _edge = edge; _cards = cards; _expand = expand; _addFiles = addFiles; _addText = addText;
        window.AllowDrop = edge.AllowDrop = true;
        window.PreviewDragEnter += Over; window.PreviewDragOver += Over; window.PreviewDragLeave += Leave; window.PreviewDrop += Drop;
        edge.PreviewDragEnter += EdgeOver; edge.PreviewDragOver += EdgeOver; edge.PreviewDragLeave += EdgeLeave; edge.PreviewDrop += Drop;
        _hover.Tick += (_, _) => { _hover.Stop(); if (IsActive) _expand(); };
    }
    private static Kind GetKind(DragData data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop, false)) return Kind.Files;
            if (data.GetDataPresent(UnicodeUrl, false) || data.GetDataPresent(AnsiUrl, false)) return Kind.Link;
            if (data.GetDataPresent(DataFormats.UnicodeText, false) || data.GetDataPresent(DataFormats.Text, false)) return Kind.Text;
        }
        catch (ExternalException) { }
        return Kind.None;
    }
    private static DragDropEffects Effect(DragEventArgs e, Kind kind)
    {
        var allowed = e.AllowedEffects;
        if (kind == Kind.Files && (allowed & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        if ((allowed & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        return allowed & DragDropEffects.Link;
    }
    private bool OnCards(DragEventArgs e) => _cards.IsVisible && new Rect(_cards.RenderSize).Contains(e.GetPosition(_cards));
    private void EdgeOver(object sender, DragEventArgs e)
    {
        var kind = GetKind(e.Data); if (kind == Kind.None) return;
        e.Effects = Effect(e, kind); e.Handled = true;
        if (e.Effects == DragDropEffects.None) return;
        _lastOver = DateTimeOffset.UtcNow;
        if (!_hover.IsEnabled) _hover.Start();
    }
    private void Over(object sender, DragEventArgs e)
    {
        var kind = GetKind(e.Data); if (kind == Kind.None) return;
        _lastOver = DateTimeOffset.UtcNow; e.Effects = OnCards(e) ? Effect(e, kind) : DragDropEffects.None; e.Handled = true;
        if (e.Effects == DragDropEffects.None) { HideHint(); return; }
        _layer ??= AdornerLayer.GetAdornerLayer(_cards);
        if (_hint == null && _layer != null) { _hint = new DropHint(_cards); _layer.Add(_hint); }
        _hint?.Show(kind);
    }
    private void Leave(object sender, DragEventArgs e)
    {
        if (new Rect(_window.RenderSize).Contains(e.GetPosition(_window))) return;
        _lastOver = DateTimeOffset.MinValue; HideHint();
    }
    private void EdgeLeave(object sender, DragEventArgs e) { _hover.Stop(); }
    private void Drop(object sender, DragEventArgs e)
    {
        var kind = GetKind(e.Data); if (kind == Kind.None) return;
        bool accept = sender == _edge || OnCards(e); _hover.Stop(); HideHint(); _lastOver = DateTimeOffset.MinValue;
        var effect = accept ? Effect(e, kind) : DragDropEffects.None;
        e.Effects = DragDropEffects.None; e.Handled = true;
        if (effect == DragDropEffects.None) return;
        App.Try(() =>
        {
            if (kind == Kind.Files)
            {
                if (e.Data.GetData(DataFormats.FileDrop, false) is not string[] { Length: > 0 } paths) return;
                var files = paths.Select(path => Path.GetFullPath(path)).Select(path => (Path: path, IsFolder: Directory.Exists(path))).ToArray();
                if (files.Any(file => !file.IsFolder && !File.Exists(file.Path))) throw new IOException("有文件已移动、删除或尚未保存到本地，请从文件夹中重新拖入。");
                _addFiles(files);
            }
            else
            {
                var content = ReadContent(e.Data, kind);
                if (string.IsNullOrWhiteSpace(content)) return;
                _addText(content);
            }
            e.Effects = effect;
            _expand();
        });
    }
    private static string? ReadContent(DragData data, Kind kind)
    {
        if (kind == Kind.Link)
        {
            // The native URL holds the target even when plain text holds the link label.
            var url = ReadString(data, UnicodeUrl, unicode: true) ?? ReadString(data, AnsiUrl, unicode: false);
            if (url != null && ContentLinks.Parse(url) != null) return url;
        }
        return ReadString(data, DataFormats.UnicodeText, unicode: true) ?? ReadString(data, DataFormats.Text, unicode: false);
    }
    private static string? ReadString(DragData data, string format, bool unicode)
    {
        // Read the native string directly. WPF also supports serialized objects in custom formats.
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject native) return null;
        var request = new FORMATETC
        {
            cfFormat = unchecked((short)DataFormats.GetDataFormat(format).Id),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL
        };
        STGMEDIUM medium;
        try
        {
            if (native.QueryGetData(ref request) != 0) return null;
            native.GetData(ref request, out medium);
        }
        catch (ExternalException) { return null; }
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL) return null;
            var bytes = GlobalSize(medium.unionmember).ToUInt64();
            int unit = unicode ? sizeof(char) : sizeof(byte);
            if (bytes < (ulong)unit || bytes > MaxTextBytes) return null;
            var pointer = GlobalLock(medium.unionmember);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                int length = 0;
                int capacity = (int)bytes / unit;
                while (length < capacity && (unicode ? Marshal.ReadInt16(pointer, length * unit) : Marshal.ReadByte(pointer, length)) != 0)
                    length++;
                if (length == 0 || length == capacity) return null;
                return unicode ? Marshal.PtrToStringUni(pointer, length) : Marshal.PtrToStringAnsi(pointer, length);
            }
            finally { GlobalUnlock(medium.unionmember); }
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    private void HideHint() { if (_hint != null) { _layer?.Remove(_hint); _hint = null; } }
    public void Dispose()
    {
        _hover.Stop(); HideHint();
        _window.PreviewDragEnter -= Over; _window.PreviewDragOver -= Over; _window.PreviewDragLeave -= Leave; _window.PreviewDrop -= Drop;
        _edge.PreviewDragEnter -= EdgeOver; _edge.PreviewDragOver -= EdgeOver; _edge.PreviewDragLeave -= EdgeLeave; _edge.PreviewDrop -= Drop;
    }
    private sealed class DropHint : Adorner
    {
        public DropHint(UIElement owner) : base(owner) { IsHitTestVisible = false; }
        private Kind _kind;
        public void Show(Kind kind)
        {
            if (_kind == kind) return;
            _kind = kind;
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext context)
        {
            var bounds = new Rect(2, 2, Math.Max(0, RenderSize.Width - 4), Math.Max(0, RenderSize.Height - 4));
            var fill = Ui.Brush("Paper").CloneCurrentValue(); fill.Opacity = .94;
            context.DrawRoundedRectangle(fill, new Pen(Ui.Brush("ControlLine"), 1) { DashStyle = DashStyles.Dash }, bounds, 10, 10);
            var message = _kind switch
            {
                Kind.Files => "松开以添加文件入口",
                Kind.Link => "松开以保存网址",
                _ => "松开以保存文字"
            };
            var text = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Ui.TaskFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, Ui.Brush("Ink"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(text, new Point(Math.Max(8, (RenderSize.Width - text.Width) / 2), Math.Max(6, (RenderSize.Height - text.Height) / 2)));
        }
    }
}
