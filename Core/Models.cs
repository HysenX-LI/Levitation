using System.Text.Json;
using System.Text.Json.Serialization;

namespace Levitation.Core;

// Unknown optional fields survive a read/write cycle across compatible releases.
public abstract class PersistentRecord
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppState : PersistentRecord
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public List<TaskItem> Tasks { get; set; } = [];
    public List<ContentCard> Cards { get; set; } = [];
    public List<Routine> Routines { get; set; } = [];
    public List<Completion> History { get; set; } = [];
    public List<TrashItem> Trash { get; set; } = [];
    public HashSet<string> SuppressedOccurrences { get; set; } = [];
    public List<string> ImportedSources { get; set; } = [];
    public Preferences Preferences { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<Guid, byte[]>? PastedImages { get; set; }
}

public enum TaskActivity { NotStarted, Running, Paused }

public class TaskItem : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TaskActivity Activity { get; set; }
    public List<Subtask> Subtasks { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletionId { get; set; }
    public Guid? RoutineId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? OccurrenceKey { get; set; }
    public bool Repeats { get; set; }
}

public sealed class Subtask : TaskItem
{
    public bool IsCompleted { get; set; }
}

public sealed class ContentCard : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public string? FilePath { get; set; }
    public bool IsFolder { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsPastedImage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public bool IsFileReference => FilePath != null;
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FilePath == null ? Content : LocalFileLinks.Name(FilePath) : Name;
    [JsonIgnore] public Uri? Link => IsFileReference || IsPastedImage ? null : ContentLinks.Parse(Content);
}

public enum RepeatFrequency { Once, Daily, Weekly }

public sealed class Routine : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public RepeatFrequency Frequency { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public List<DayOfWeek> Weekdays { get; set; } = [];

    public bool IsDue(DateOnly day) => day >= StartDate &&
        (Frequency != RepeatFrequency.Weekly || Weekdays.Contains(day.DayOfWeek));

    [JsonIgnore] public string Description => Frequency switch
    {
        RepeatFrequency.Once => $"{StartDate:yyyy-MM-dd} · 仅一次",
        RepeatFrequency.Daily => $"每天 · {StartDate:yyyy-MM-dd} 起",
        _ => $"每周{string.Join("、", Weekdays.OrderBy(d => ((int)d + 6) % 7).Select(d => "日一二三四五六"[(int)d]))} · {StartDate:yyyy-MM-dd} 起"
    };
}

public sealed class Completion : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
    public Guid? RoutineId { get; set; }
    public bool IsLegacy { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CompletionSubtask>? Subtasks { get; set; }
    [JsonIgnore] public DateOnly Day => DateOnly.FromDateTime(CompletedAt.Date);
}

public sealed record CompletionSubtask(Guid TaskId, string Title, int Depth, bool IsCompleted, DateTimeOffset? CompletedAt);

public sealed class TrashItem : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.Now;
    public TaskItem? Task { get; set; }
    public ContentCard? Card { get; set; }
    public int OriginalIndex { get; set; }
    public Guid? ParentTaskId { get; set; }
    public string? ParentTitle { get; set; }
    public bool SubtaskWasCompleted { get; set; }
    [JsonIgnore] public string Title => Task?.Title ?? Card?.DisplayName ?? "";
    [JsonIgnore] public string Kind => ParentTaskId.HasValue ? "子待办" : Task != null ? "待办" : "常用内容";
}

public enum DockTransition { Scale, Slide }

public sealed class Preferences : PersistentRecord
{
    public double AutoHideSeconds { get; set; } = .5;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DockTransition DockTransition { get; set; }
    public string Theme { get; set; } = "System";
    public bool QuickCaptureEnabled { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuickCaptureShortcut? QuickCaptureShortcut { get; set; }
    public string? MonitorName { get; set; }
    public double Width { get; set; } = 340;
    public double? Height { get; set; }
    public bool TasksExpanded { get; set; } = true;
    public bool CardsExpanded { get; set; } = true;
    public double TasksHeight { get; set; } = 320;
    public double CardsHeight { get; set; } = 220;
    public double DockPosition { get; set; } = .5;
    public bool IsDocked { get; set; } = true;
    public double FloatingX { get; set; } = 1;
    public double FloatingY { get; set; } = .5;
}

public static class ContentLinks
{
    public static Uri? Parse(string content)
    {
        var text = content.Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace)) return null;
        bool explicitWeb = text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                           text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        if (!explicitWeb)
        {
            if (text.Contains("://") || text.Contains('@') || !text.Split('/')[0].Contains('.')) return null;
            text = "https://" + text;
        }
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "https" || uri.Scheme == "http") &&
               !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    }
}
