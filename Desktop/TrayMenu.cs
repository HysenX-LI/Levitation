using System.ComponentModel;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Levitation.Desktop;

internal enum TrayAction { Expand, Routines, Settings, Exit }

// Keep the native tray menu's focus, keyboard and dismissal behavior, while
// painting it from exactly the same semantic resources as the WPF menus.
internal sealed class TrayMenu : Forms.ContextMenuStrip
{
    private readonly Drawing.Font _menuFont = new("Microsoft YaHei UI", 9.75f);
    private bool _ready, _arranging;

    // ToolStripDropDownMenu resets Padding during every layout and offsets menu
    // items by that padding. Put the inset in item margins instead, so drawing
    // and hit testing use the same full-width rectangle.
    protected override Forms.Padding DefaultPadding => Forms.Padding.Empty;

    public TrayMenu()
    {
        ShowImageMargin = false; ShowCheckMargin = false; Font = _menuFont;
        DropShadowEnabled = true; AutoSize = false; _ready = true;
        Ui.ThemeChanged += ApplyTheme;
        ApplyTheme();
    }

    public void AddAction(TrayAction icon, string label, Action action)
    {
        var item = new Forms.ToolStripMenuItem(label) { Tag = icon, ShowShortcutKeys = false, AutoToolTip = false, AutoSize = false };
        item.Click += (_, _) => App.Try(action); Items.Add(item);
    }

    protected override void OnLayout(Forms.LayoutEventArgs e)
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            if (_ready) ArrangeRows();
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
    private void ArrangeRows()
    {
        int Px(double value) => (int)Math.Round(value * DeviceDpi / 96d);
        int inset = Px(6), width = Px(192), height = 0;
        var items = Items.Cast<Forms.ToolStripItem>().Where(item => item.Available).ToArray();
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index]; bool separator = item is Forms.ToolStripSeparator;
            int top = index == 0 ? inset : separator ? Px(6) : items[index - 1] is Forms.ToolStripSeparator ? 0 : Px(2);
            int bottom = index == items.Length - 1 ? inset : separator ? Px(6) : 0;
            item.AutoSize = false; item.Padding = Forms.Padding.Empty;
            item.Margin = new Forms.Padding(separator ? 0 : inset, top, separator ? 0 : inset, bottom);
            item.Size = new Drawing.Size(separator ? width : width - 2 * inset, separator ? Px(1) : Px(32));
            height += item.Height + top + bottom;
        }
        ClientSize = new Drawing.Size(width, Math.Max(2 * inset, height));
    }

    private void ApplyTheme()
    {
        if (IsDisposed || Disposing) return;
        static Drawing.Color Color(string token)
        {
            var value = ((SolidColorBrush)Ui.Brush(token)).Color;
            return Drawing.Color.FromArgb(value.A, value.R, value.G, value.B);
        }
        var colors = new MenuColors(Color("TraySurface"), Color("Ink"), Color("Muted"), Color("TrayIcon"), Color("MenuHover"), Color("TrayLine"));
        SuspendLayout();
        try
        {
            BackColor = colors.Surface; ForeColor = colors.Ink; Renderer = new MenuRenderer(colors);
            foreach (Forms.ToolStripItem item in Items)
            {
                item.ForeColor = colors.Ink; item.BackColor = colors.Surface;
                item.TextAlign = Drawing.ContentAlignment.MiddleLeft;
            }
        }
        finally { ResumeLayout(true); }
        PerformLayout();
        Invalidate(true);
    }

    protected override void OnOpening(CancelEventArgs e) { ApplyTheme(); base.OnOpening(e); }
    protected override void OnOpened(EventArgs e) { Ui.TrayMenuOpen = true; base.OnOpened(e); }
    protected override void OnClosed(Forms.ToolStripDropDownClosedEventArgs e) { Ui.TrayMenuOpen = false; base.OnClosed(e); }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var outline = Round(new Drawing.RectangleF(0, 0, ClientSize.Width, ClientSize.Height), (float)(12 * DeviceDpi / 96d));
        var previous = Region; Region = new Drawing.Region(outline); previous?.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _ready = false; Ui.ThemeChanged -= ApplyTheme; Ui.TrayMenuOpen = false; }
        try { base.Dispose(disposing); }
        finally { if (disposing) _menuFont.Dispose(); }
    }

    private static Drawing2D.GraphicsPath Round(Drawing.RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    private readonly record struct MenuColors(Drawing.Color Surface, Drawing.Color Ink, Drawing.Color Muted, Drawing.Color Icon, Drawing.Color Hover, Drawing.Color Line);

    private sealed class MenuRenderer(MenuColors colors) : Forms.ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(colors.Surface); e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            float scale = e.ToolStrip.DeviceDpi / 96f, half = scale / 2;
            using var path = Round(new Drawing.RectangleF(half, half, e.ToolStrip.ClientSize.Width - scale, e.ToolStrip.ClientSize.Height - scale), 12 * scale);
            using var pen = new Drawing.Pen(colors.Line, scale); e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = Round(new Drawing.RectangleF(0, 0, e.Item.Width, e.Item.Height), (float)(7 * (e.Item.Owner?.DeviceDpi ?? 96) / 96d));
            using var brush = new Drawing.SolidBrush(colors.Hover); e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item == null || string.IsNullOrEmpty(e.Text)) return;
            double scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96d;
            int Px(double value) => (int)Math.Round(value * scale);
            if (e.Item.Tag is TrayAction icon)
                DrawIcon(e.Graphics, icon, new Drawing.RectangleF(Px(12), (e.Item.Height - Px(16)) / 2f, Px(16), Px(16)),
                    !e.Item.Enabled ? colors.Muted : e.Item.Selected || e.Item.Pressed ? colors.Ink : colors.Icon);
            var bounds = new Drawing.Rectangle(Px(39), 0, Math.Max(1, e.Item.Width - Px(51)), e.Item.Height);
            Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, e.Item.Enabled ? colors.Ink : colors.Muted,
                Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPadding | Forms.TextFormatFlags.EndEllipsis);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Item == null) return;
            float scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96f;
            using var pen = new Drawing.Pen(colors.Line, scale);
            float left = 16 * scale - e.Item.Bounds.Left;
            float right = (e.Item.Owner?.ClientSize.Width ?? e.Item.Width) - 16 * scale - e.Item.Bounds.Left;
            e.Graphics.DrawLine(pen, left, e.Item.Height / 2f, right, e.Item.Height / 2f);
        }
        private static void DrawIcon(Drawing.Graphics graphics, TrayAction icon, Drawing.RectangleF bounds, Drawing.Color color)
        {
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
                graphics.TranslateTransform(bounds.Left, bounds.Top); graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
                using var pen = new Drawing.Pen(color, 1.6f) { StartCap = Drawing2D.LineCap.Round, EndCap = Drawing2D.LineCap.Round, LineJoin = Drawing2D.LineJoin.Round };
                if (icon is TrayAction.Expand or TrayAction.Routines)
                {
                    using var window = Round(new Drawing.RectangleF(4, 5, 16, icon == TrayAction.Expand ? 14 : 15), 3);
                    graphics.DrawPath(pen, window);
                    if (icon == TrayAction.Expand)
                    {
                        graphics.DrawLine(pen, 10, 5, 10, 19);
                        graphics.DrawLines(pen, new[] { new Drawing.PointF(15, 9), new Drawing.PointF(12, 12), new Drawing.PointF(15, 15) });
                    }
                    else
                    {
                        graphics.DrawLine(pen, 8, 3, 8, 7); graphics.DrawLine(pen, 16, 3, 16, 7); graphics.DrawLine(pen, 4, 10, 20, 10);
                        graphics.DrawLine(pen, 8, 14, 11, 14); graphics.DrawLine(pen, 8, 17, 14, 17);
                    }
                }
                else if (icon == TrayAction.Settings)
                {
                    graphics.DrawPolygon(pen, new[] {
                        new Drawing.PointF(9.5f,3), new Drawing.PointF(8.8f,5.2f), new Drawing.PointF(6.8f,6.1f), new Drawing.PointF(4.6f,5.6f),
                        new Drawing.PointF(2.6f,9), new Drawing.PointF(4.1f,10.7f), new Drawing.PointF(4.1f,13.3f), new Drawing.PointF(2.6f,15),
                        new Drawing.PointF(4.6f,18.4f), new Drawing.PointF(6.8f,17.9f), new Drawing.PointF(8.8f,18.8f), new Drawing.PointF(9.5f,21),
                        new Drawing.PointF(14.3f,21), new Drawing.PointF(15,18.8f), new Drawing.PointF(17,17.9f), new Drawing.PointF(19.2f,18.4f),
                        new Drawing.PointF(21.2f,15), new Drawing.PointF(19.7f,13.3f), new Drawing.PointF(19.7f,10.7f), new Drawing.PointF(21.2f,9),
                        new Drawing.PointF(19.2f,5.6f), new Drawing.PointF(17,6.1f), new Drawing.PointF(15,5.2f), new Drawing.PointF(14.3f,3) });
                    graphics.DrawEllipse(pen, 9, 9, 6, 6);
                }
                else { graphics.DrawLine(pen, 12, 3, 12, 11); graphics.DrawArc(pen, 4, 4, 16, 16, -45, 270); }
            }
            finally { graphics.Restore(state); }
        }
        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            if (e.Item == null) return;
            float x = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2f, y = e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2f;
            double scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96d;
            float unit = (float)(3 * scale);
            using var pen = new Drawing.Pen(e.Item.Enabled ? colors.Ink : colors.Muted, (float)(1.2 * scale))
            { StartCap = Drawing2D.LineCap.Round, EndCap = Drawing2D.LineCap.Round, LineJoin = Drawing2D.LineJoin.Round };
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[] { new Drawing.PointF(x - unit / 2, y - unit), new Drawing.PointF(x + unit / 2, y), new Drawing.PointF(x - unit / 2, y + unit) });
        }
    }
}
