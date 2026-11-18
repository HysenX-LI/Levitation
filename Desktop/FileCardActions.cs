using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace Levitation.Desktop;

internal sealed class FileCardActions(Window owner, TaskService service, DataStore store)
{
    private static readonly System.Threading.SemaphoreSlim ThumbnailSlots = new(2, 2);
    private const string ImageGlyph = "M 2,2 H 14 V 14 H 2 Z M 2,11 L 6,7 L 9,10 L 11,8 L 14,11 M 10,5 H 10.1";
    private const string FileGlyph = "M 4,1.5 H 9 L 13,5.5 V 14.5 H 4 Z M 9,1.5 V 5.5 H 13 M 6,9 H 11 M 6,11.5 H 10";
    private const string FolderGlyph = "M 1.5,4.5 V 13 H 14.5 V 4.5 H 7 L 5,2.5 H 1.5 Z";
    public Border CreateRow(ContentCard card)
    {
        string path = PathFor(card);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        bool isImage = !card.IsFolder && LocalFileLinks.IsImage(path);
        icon.SetResourceReference(Border.BackgroundProperty, "Soft"); icon.Child = TaskControls.Stroke(card.IsFolder ? FolderGlyph : isImage ? ImageGlyph : FileGlyph, "Muted", 1.2); row.Children.Add(icon);
        if (isImage) LoadThumbnail(icon, path);
        var title = Ui.Text("", 12); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = card.IsPastedImage ? card.DisplayName : path;
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(card.DisplayName)) { TextDecorations = null };
        link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink"); link.Click += (_, _) => App.Try(() => Open(card)); title.Inlines.Add(link);
        Grid.SetColumn(title, 1); row.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.Icon(Ui.Copy, "复制文件路径", () => System.Windows.Clipboard.SetText(path)));
        buttons.Children.Add(Ui.Icon(Ui.Delete, card.IsPastedImage ? "移入垃圾堆" : "删除入口（保留原文件）", () => service.DeleteCard(card.Id))); Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
        var border = new Border { Child = row, Padding = new Thickness(2, 5, 0, 5), Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1), ToolTip = title.ToolTip };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        var menu = Ui.CreateMenu();
        void Item(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => App.Try(action); menu.Items.Add(item); }
        Item(card.IsFolder ? "打开文件夹" : isImage ? "打开图片" : "打开文件", () => Open(card)); Item("打开所在文件夹", () => OpenFolder(card)); Item("复制路径", () => System.Windows.Clipboard.SetText(path));
        if (card.IsPastedImage) Item("修改名称", () => RenameImage(card));
        else Item("修改名称或重新关联", () => Edit(card));
        menu.Items.Add(new Separator()); Item(card.IsPastedImage ? "移入垃圾堆" : "移入垃圾堆（保留原文件）", () => service.DeleteCard(card.Id)); border.ContextMenu = menu;
        return border;
    }
    private static void LoadThumbnail(Border icon, string path)
    {
        // Decode on a worker and close the stream immediately. Task/card updates
        // reuse the existing row, so checking a todo never reopens image files.
        icon.Loaded += async (_, _) =>
        {
            if (icon.Tag != null) return; icon.Tag = true;
            await ThumbnailSlots.WaitAsync();
            BitmapSource? image;
            try { image = await System.Threading.Tasks.Task.Run<BitmapSource?>(() =>
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 64; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException) { return null; }
            }); } finally { ThumbnailSlots.Release(); }
            if (image != null && icon.IsLoaded) { icon.Clip = new RectangleGeometry(new Rect(0, 0, 28, 28), 5, 5); icon.Child = new Image { Source = image, Stretch = Stretch.UniformToFill }; }
        };
    }
    private string PathFor(ContentCard card) => card.IsPastedImage ? store.PastedImagePath(card.Id) : card.FilePath!;
    private void Open(ContentCard card)
    {
        string path = PathFor(card);
        if (!(card.IsFolder ? Directory.Exists(path) : File.Exists(path))) { Missing(card); return; }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
    private void OpenFolder(ContentCard card)
    {
        string target = PathFor(card);
        string? path = Path.GetDirectoryName(target.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(path) && Directory.Exists(target)) path = target;
        if (path == null || !Directory.Exists(path)) { Missing(card); return; }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
    private void Missing(ContentCard card)
    {
        if (card.IsPastedImage) throw new FileNotFoundException("找不到此截图。可从包含截图的完整备份恢复，或重新粘贴图片。");
        var dialog = Ui.Dialog("文件位置已变化", owner, 420);
        var path = Ui.Text(card.FilePath!, 11, true); path.MaxHeight = 80; path.TextTrimming = TextTrimming.CharacterEllipsis;
        var cancel = Ui.Action("取消", () => dialog.DialogResult = false); cancel.IsCancel = true;
        var associate = Ui.Action("重新关联", () => { dialog.DialogResult = true; Edit(card); }, true);
        dialog.Content = Ui.Scroll(Ui.Stack(Ui.Text("文件可能已移动、删除，或当前无法访问。"), path, Ui.Buttons(cancel, associate))); ((FrameworkElement)dialog.Content).Margin = new Thickness(20); dialog.ShowDialog();
    }
    private void RenameImage(ContentCard card)
    {
        var dialog = Ui.Dialog("修改截图名称", owner, 420);
        var name = Ui.Input("显示名称，可选", card.Name);
        name.MaxLength = 80;
        var cancel = Ui.Action("取消", () => dialog.DialogResult = false);
        cancel.IsCancel = true;
        var save = Ui.Action("保存", () => { service.RenamePastedImage(card.Id, name.Text); dialog.DialogResult = true; }, true);
        save.IsDefault = true;
        var fields = Ui.Stack(Ui.Field("显示名称（可选）", name), Ui.Buttons(cancel, save));
        fields.Margin = new Thickness(20);
        dialog.Content = Ui.Scroll(fields);
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        dialog.ShowDialog();
    }
    public void Edit(ContentCard card)
    {
        var dialog = Ui.Dialog("文件入口", owner, 460); var name = Ui.Input("显示名称，可选", card.Name); string path = card.FilePath!;
        var location = Ui.Input("关联位置", path); location.IsReadOnly = true;
        var choose = Ui.Action(card.IsFolder ? "更换文件夹…" : "更换文件…", () =>
        {
            if (card.IsFolder)
            {
                using var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "重新关联文件夹", UseDescriptionForTitle = true };
                if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) path = picker.SelectedPath;
            }
            else
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "重新关联文件", CheckFileExists = true };
                if (picker.ShowDialog(dialog) == true) path = picker.FileName;
            }
            location.Text = path;
        });
        var cancel = Ui.Action("取消", () => dialog.DialogResult = false); cancel.IsCancel = true;
        var save = Ui.Action("保存", () => { service.UpdateFileCard(card.Id, name.Text, path, card.IsFolder); dialog.DialogResult = true; }, true); save.IsDefault = true;
        var fields = Ui.Stack(Ui.Field("显示名称（可选）", name), Ui.Field("关联位置", location), choose, Ui.Buttons(cancel, save)); fields.Margin = new Thickness(20); dialog.Content = Ui.Scroll(fields);
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); }; dialog.ShowDialog();
    }
}
