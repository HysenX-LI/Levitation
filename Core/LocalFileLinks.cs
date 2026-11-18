namespace Levitation.Core;

public static class LocalFileLinks
{
    // Pure Windows path handling, also testable on the non-Windows build host.
    // File references never pass through the web URL parser or a command line.
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim().Replace('/', '\\');
        if (path.Any(c => c < 32 || "<>\"|?*".Contains(c))) return null;
        bool drive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        bool unc = path.StartsWith("\\\\", StringComparison.Ordinal) && path.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
        if (!drive && !unc || path.Skip(drive ? 2 : 0).Contains(':')) return null;
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or "..")) return null;
        return drive ? parts.Length == 1 ? parts[0] + @"\" : string.Join('\\', parts) : @"\\" + string.Join('\\', parts);
    }
    public static string Name(string path) => path.TrimEnd('\\').Split('\\').Last();
    public static bool IsImage(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
