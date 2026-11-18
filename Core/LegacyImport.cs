using System.Text.Json;

namespace Levitation.Core;

/// <summary>Imports the old portable files without modifying or deleting the originals.</summary>
public static class LegacyImport
{
    public static int ReadInto(AppState state, string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (state.ImportedSources.Contains(directory, StringComparer.OrdinalIgnoreCase)) return 0;
        int count = 0;
        foreach (var row in Rows(directory, "todos.jsonl").OrderBy(r => Int(r, "Order")))
        {
            var title = Text(row, "Title"); if (string.IsNullOrWhiteSpace(title)) continue;
            var id = Id(row);
            if (state.Tasks.Any(t => t.Id == id)) continue;
            state.Tasks.Add(new TaskItem { Id = id, Title = title, CreatedAt = Timestamp(row, "CreatedAt") }); count++;
        }
        // In the old app deletion meant completion; preserve these records as history.
        foreach (var row in Rows(directory, "recycle_bin.jsonl"))
        {
            var title = Text(row, "Title"); if (string.IsNullOrWhiteSpace(title)) continue;
            var taskId = Id(row);
            if (state.History.Any(h => h.IsLegacy && h.TaskId == taskId)) continue;
            state.History.Add(new Completion { TaskId = taskId, Title = title,
                CompletedAt = Timestamp(row, "DeletedAt"), IsLegacy = true }); count++;
        }
        var phraseFile = File.Exists(Path.Combine(directory, "phrases.jsonl")) ? "phrases.jsonl" : "quick_phrases.jsonl";
        foreach (var row in Rows(directory, phraseFile))
        {
            var content = Text(row, "Content"); if (string.IsNullOrWhiteSpace(content)) continue;
            var id = Id(row); if (state.Cards.Any(c => c.Id == id)) continue;
            state.Cards.Add(new ContentCard { Id = id, Content = content }); count++;
        }
        if (state.Revision == 0)
        {
            var settingsPath = Path.Combine(directory, "settings.jsonl");
            if (!File.Exists(settingsPath)) settingsPath = Path.Combine(directory, "settings.json");
            if (File.Exists(settingsPath))
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath).TrimStart('\uFEFF'));
                var theme = Text(settings.RootElement, "ThemeMode");
                if (theme is "System" or "Light" or "Dark") state.Preferences.Theme = theme;
            }
        }
        // Automatic clipboard capture has been removed. Its legacy file is left untouched.
        state.ImportedSources.Add(directory);
        return count;
    }

    private static IEnumerable<JsonElement> Rows(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path)) yield break;
        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument document;
            try { document = JsonDocument.Parse(line.TrimStart('\uFEFF')); }
            catch (JsonException ex) { throw new InvalidDataException($"{name} 第 {lineNumber} 行损坏，迁移已停止；原文件未改动。", ex); }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{name} 第 {lineNumber} 行不是有效记录。");
                yield return document.RootElement.Clone();
            }
        }
    }

    private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static Guid Id(JsonElement row) => Guid.TryParse(Text(row, "Id"), out var id) && id != Guid.Empty ? id : Guid.NewGuid();
    private static int Int(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static DateTimeOffset Timestamp(JsonElement row, string key) => DateTimeOffset.TryParse(Text(row, key), out var value) ? value : DateTimeOffset.Now;
}
