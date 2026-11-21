using System.Text.Json;
using System.Text.Json.Serialization;

namespace Levitation.Core;

public sealed class UnsupportedSchemaException(int version) : Exception(
    $"数据由较新的 Levitation 创建（格式版本 {version}）。请使用较新版程序打开；本版本不会覆盖数据。");

/// <summary>
/// Version-independent user data, exclusive process lock, durable atomic replacement,
/// last-known-good copy and daily snapshots. Failed loads never become empty saves.
/// </summary>
public sealed partial class DataStore : IDisposable
{
    private const int CurrentSchema = 2;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    // Intentionally stable across the WinKit -> Levitation rename and version resets.
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinKit");

    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "state.json");
    public string PreviousPath => Path.Combine(DirectoryPath, "state.previous.json");
    public string BackupsPath => Path.Combine(DirectoryPath, "Backups");
    public AppState State { get; private set; }
    public string? RecoverySource { get; private set; }
    private readonly FileStream _lock;
    private bool _preserveCorruptPrimary;
    private bool _disposed;

    public DataStore(string? directory = null, string? legacyDirectory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? DefaultDirectory);
        Directory.CreateDirectory(DirectoryPath);
        _lock = new FileStream(Path.Combine(DirectoryPath, "state.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            State = Load();
            if (!File.Exists(FilePath))
            {
                if (RecoverySource == null && legacyDirectory != null) LegacyImport.ReadInto(State, legacyDirectory);
                Save(State);
            }
        }
        catch { _lock.Dispose(); throw; }
    }

    public static AppState ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            throw new InvalidDataException("文件不是完整的 Levitation 数据备份。");
        if (number > CurrentSchema) throw new UnsupportedSchemaException(number);
        foreach (var required in new[] { "tasks", "cards", "routines", "history", "trash", "preferences" })
            if (!root.TryGetProperty(required, out _)) throw new InvalidDataException($"数据文件缺少 {required}，已停止读取以保护原文件。");
        var state = JsonSerializer.Deserialize<AppState>(bytes, Json)
            ?? throw new InvalidDataException("数据文件为空或内容无效。");
        Validate(state);
        return state;
    }

    public static void Validate(AppState state)
    {
        if (state.SchemaVersion > CurrentSchema) throw new UnsupportedSchemaException(state.SchemaVersion);
        if (state.SchemaVersion < 1 || state.Tasks == null || state.Cards == null ||
            state.Routines == null || state.History == null || state.Trash == null ||
            state.Preferences == null || state.SuppressedOccurrences == null || state.ImportedSources == null)
            throw new InvalidDataException("数据结构不完整，已停止读取以保护原文件。");
        var taskIds = new HashSet<Guid>();
        if (state.Tasks.Any(t => !ValidTask(t, 0, taskIds)) ||
            state.Cards.Any(c => !ValidCard(c)) ||
            state.Routines.Any(r => r == null || !r.IsValid) ||
            state.History.Any(h => h == null || h.Title == null || (h.Subtasks?.Any(c => c == null || c.TaskId == Guid.Empty ||
                c.Title == null || c.Depth < 1 || c.Depth > TaskTree.MaximumDepth) ?? false)) ||
            state.Trash.Any(t => t == null || (t.Task == null) == (t.Card == null) || (t.Task != null && !ValidTask(t.Task)) || (t.Card != null && !ValidCard(t.Card)) ||
                (t.ParentTaskId.HasValue && (t.ParentTaskId == Guid.Empty || t.Task == null))) ||
            state.Tasks.Select(t => t.Id).Distinct().Count() != state.Tasks.Count ||
            state.Cards.Select(c => c.Id).Distinct().Count() != state.Cards.Count ||
            state.Routines.Select(r => r.Id).Distinct().Count() != state.Routines.Count)
            throw new InvalidDataException("数据条目无效或编号冲突，原文件未被修改。");
    }
    private static bool ValidTask(TaskItem? task, int depth = 0, HashSet<Guid>? ids = null)
    {
        ids ??= new HashSet<Guid>();
        return depth <= TaskTree.MaximumDepth && task != null && task.Id != Guid.Empty && ids.Add(task.Id) && task.Title != null &&
            Enum.IsDefined(task.Activity) && task.Subtasks != null && task.Subtasks.All(c => ValidTask(c, depth + 1, ids));
    }
    private static bool ValidCard(ContentCard? card) => card != null && card.Id != Guid.Empty && card.Content != null && card.Name != null &&
        (!card.IsPastedImage || (card.FilePath == null && !card.IsFolder)) &&
        (card.FilePath == null || (LocalFileLinks.Normalize(card.FilePath) is { } path && path == card.FilePath && card.Content == path));

    private AppState Load()
    {
        var candidates = new List<string>();
        if (File.Exists(FilePath)) candidates.Add(FilePath);
        if (File.Exists(PreviousPath)) candidates.Add(PreviousPath);
        if (Directory.Exists(BackupsPath)) candidates.AddRange(DailyBackups().OrderByDescending(Path.GetFileName));
        if (candidates.Count == 0) return new AppState();
        foreach (var path in candidates)
        {
            try
            {
                var result = ReadFile(path);
                ImportPastedImages(result);
                if (path != FilePath)
                {
                    RecoverySource = path;
                    _preserveCorruptPrimary = File.Exists(FilePath);
                }
                return result;
            }
            catch (UnsupportedSchemaException) { throw; }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException) { }
        }
        throw new InvalidDataException("主数据与备份均无法读取。为保护原文件，Levitation 不会创建空数据覆盖。请从备份恢复，或保留整个数据目录后联系维护者。");
    }

    public AppState Snapshot() => JsonSerializer.Deserialize<AppState>(JsonSerializer.SerializeToUtf8Bytes(State, Json), Json)!;

    public void Save(AppState next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(next);
        // Older releases reject a newer schema before attempting backup recovery.
        if (next.Routines.Any(rule => rule.RequiresNewSchema)) next.SchemaVersion = CurrentSchema;
        var temp = Path.Combine(DirectoryPath, $"state.{Guid.NewGuid():N}.tmp");
        next.Revision = State == null ? 1 : State.Revision + 1;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); }
            if (State is { SchemaVersion: 1 } && next.SchemaVersion == CurrentSchema)
            {
                var original = !_preserveCorruptPrimary && File.Exists(FilePath) ? FilePath : RecoverySource;
                if (original != null)
                    File.Copy(original, Path.Combine(DirectoryPath, $"before-schema-{CurrentSchema}-{Guid.NewGuid():N}.json"));
            }
            // Backup failures abort the commit; never silently sacrifice the last good data.
            if (File.Exists(FilePath))
            {
                if (_preserveCorruptPrimary)
                {
                    File.Move(FilePath, Path.Combine(DirectoryPath, $"state.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"));
                }
                else
                {
                    Directory.CreateDirectory(BackupsPath);
                    var daily = Path.Combine(BackupsPath, $"{DateTime.Now:yyyy-MM-dd}.json");
                    if (!File.Exists(daily)) File.Copy(FilePath, daily);
                }
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, PreviousPath, true);
            else File.Move(temp, FilePath);
            State = next;
            _preserveCorruptPrimary = false;
            // Retention only touches managed daily snapshots, never history or user exports.
            if (Directory.Exists(BackupsPath))
                foreach (var old in DailyBackups().OrderByDescending(Path.GetFileName).Skip(14))
                    try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Export(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (destination.Equals(FilePath, StringComparison.OrdinalIgnoreCase) ||
            destination.Equals(PreviousPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择数据目录以外的备份文件位置。");
        var export = Snapshot();
        export.PastedImages = CollectPastedImages(export);
        var temp = Path.Combine(Path.GetDirectoryName(destination)!, $".levitation-export-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough)) { JsonSerializer.Serialize(file, export, Json); file.Flush(true); }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Restore(string source, Action<AppState>? prepare = null)
    {
        var restored = ReadFile(source); // Validate before touching live data.
        prepare?.Invoke(restored);
        var before = Path.Combine(DirectoryPath, $"before-restore-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json");
        Export(before);
        var added = ImportPastedImages(restored);
        try { Save(restored); }
        catch
        {
            if (!ReferenceEquals(State, restored)) foreach (var id in added) DiscardPastedImage(id);
            throw;
        }
    }

    private IEnumerable<string> DailyBackups() => Directory.GetFiles(BackupsPath, "*.json").Where(path =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _));

    public void Dispose() { if (_disposed) return; _disposed = true; _lock.Dispose(); }
}
