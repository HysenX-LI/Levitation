using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;

namespace Levitation.Desktop;

internal static class ClipboardImagePaste
{
    private const string Png = "PNG";

    public static void Attach(FrameworkElement area, TextBox input, Action<byte[]> save)
    {
        bool saving = false;
        area.Focusable = true;
        area.FocusVisualStyle = null;
        area.MouseLeftButtonDown += (_, _) =>
        {
            if (!area.IsKeyboardFocusWithin) area.Focus();
        };
        area.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && TryPaste()) e.Handled = true;
        };
        CommandManager.AddPreviewCanExecuteHandler(input, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Paste || !ContainsImage()) return;
            e.CanExecute = true;
            e.Handled = true;
        });
        CommandManager.AddPreviewExecutedHandler(input, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Paste && TryPaste()) e.Handled = true;
        });

        bool TryPaste()
        {
            BitmapSource? bitmap = null;
            bool hasImage = false;
            App.Try(() =>
            {
                hasImage = ContainsImage();
                if (!hasImage || saving) return;
                var captured = ReadImage();
                if (captured == null) throw new IOException("无法读取剪贴板里的图片，请重新复制后再粘贴。");
                if ((long)captured.PixelWidth * captured.PixelHeight > DataStore.MaximumImagePixels)
                    throw new IOException("图片超过 4000 万像素，请裁小后再粘贴。");
                if (!captured.IsFrozen) captured.Freeze();
                bitmap = captured;
            });
            if (!hasImage || bitmap?.IsFrozen != true) return hasImage;
            saving = true;
            SaveImage(bitmap);
            return true;
        }

        async void SaveImage(BitmapSource bitmap)
        {
            try
            {
                var png = await System.Threading.Tasks.Task.Run(() =>
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });
                App.Try(() => save(png));
            }
            catch (Exception ex) { App.Try(() => throw new IOException("无法将剪贴板图片保存为 PNG。", ex)); }
            finally { saving = false; }
        }
    }

    private static bool ContainsImage()
    {
        try { return Clipboard.ContainsImage() || Clipboard.ContainsData(Png); }
        catch (ExternalException) { return false; }
    }

    private static BitmapSource? ReadImage()
    {
        // Windows and browsers can provide a native PNG alongside the Bitmap format.
        // Prefer PNG so alpha is preserved when the clipboard includes both.
        if (Clipboard.ContainsData(Png))
        {
            byte[]? png = Clipboard.GetData(Png) switch
            {
                byte[] bytes => bytes,
                MemoryStream stream => stream.ToArray(),
                _ => null
            };
            if (png is { Length: > 0 and <= 64 * 1024 * 1024 })
            {
                try
                {
                    using var source = new MemoryStream(png, writable: false);
                    var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    if ((long)frame.PixelWidth * frame.PixelHeight > DataStore.MaximumImagePixels)
                        throw new IOException("图片超过 4000 万像素，请裁小后再粘贴。");
                    var bitmap = new WriteableBitmap(frame);
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex) when (ex is FileFormatException or NotSupportedException or COMException) { }
            }
        }
        return Clipboard.GetImage();
    }
}
