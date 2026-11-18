using System.Buffers.Binary;

namespace Levitation.Core;

public sealed partial class DataStore
{
    private const int MaximumPngBytes = 64 * 1024 * 1024;
    public const long MaximumImagePixels = 40_000_000;

    public string PastedImagePath(Guid id) => Path.Combine(DirectoryPath, "Images", $"{id:N}.png");

    private static IEnumerable<Guid> PastedImageIds(AppState state) => state.Cards
        .Concat(state.Trash.Where(t => t.Card != null).Select(t => t.Card!))
        .Where(card => card.IsPastedImage).Select(card => card.Id).Distinct();

    private static void ValidatePng(byte[] png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 33 || png.Length > MaximumPngBytes || !png.AsSpan().StartsWith(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8)) != 13 || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("截图不是有效的 PNG，或超过 64 MB。");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
        if (width == 0 || height == 0 || (ulong)width * height > MaximumImagePixels)
            throw new InvalidDataException("截图尺寸超出支持范围。");
    }

    internal void WritePastedImage(Guid id, byte[] png)
    {
        ValidatePng(png);
        string path = PastedImagePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(png);
                file.Flush(true);
            }
            File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal void DiscardPastedImage(Guid id)
    {
        try { File.Delete(PastedImagePath(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private Dictionary<Guid, byte[]>? CollectPastedImages(AppState state)
    {
        var images = new Dictionary<Guid, byte[]>();
        foreach (var id in PastedImageIds(state))
        {
            var file = new FileInfo(PastedImagePath(id));
            if (!file.Exists) continue;
            if (file.Length > MaximumPngBytes) throw new InvalidDataException("截图文件超出可备份的大小。");
            var png = File.ReadAllBytes(file.FullName);
            ValidatePng(png);
            images.Add(id, png);
        }
        return images.Count == 0 ? null : images;
    }

    private List<Guid> ImportPastedImages(AppState state)
    {
        var added = new List<Guid>();
        if (state.PastedImages == null) return added;

        var ids = PastedImageIds(state).ToHashSet();
        try
        {
            foreach (var (id, png) in state.PastedImages)
            {
                if (!ids.Contains(id) || png == null) throw new InvalidDataException("备份中的截图与条目不匹配。");
                ValidatePng(png);
                var file = new FileInfo(PastedImagePath(id));
                if (file.Exists)
                {
                    if (file.Length != png.Length || !File.ReadAllBytes(file.FullName).AsSpan().SequenceEqual(png))
                        throw new InvalidDataException("备份中的截图与本地文件冲突。");
                    continue;
                }
                WritePastedImage(id, png);
                added.Add(id);
            }
        }
        catch
        {
            foreach (var id in added) DiscardPastedImage(id);
            throw;
        }
        state.PastedImages = null;
        return added;
    }
}
