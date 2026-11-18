namespace Levitation.Core;

public enum TaskDropPosition { Before, Inside, After }
public sealed record TaskMove(Guid SourceId, Guid TargetId, TaskDropPosition Position);
public sealed record TaskLocation(TaskItem Item, TaskItem? Parent, TaskItem Root, int Depth);

public static class TaskTree
{
    public const int MaximumDepth = 3; // Root is depth zero.

    public static IEnumerable<TaskLocation> Walk(IEnumerable<TaskItem> roots)
    {
        foreach (var root in roots)
            foreach (var location in WalkNode(root, null, root, 0)) yield return location;
    }
    private static IEnumerable<TaskLocation> WalkNode(TaskItem item, TaskItem? parent, TaskItem root, int depth)
    {
        yield return new(item, parent, root, depth);
        foreach (var child in item.Subtasks)
            foreach (var location in WalkNode(child, item, root, depth + 1)) yield return location;
    }
    public static TaskLocation? Find(IEnumerable<TaskItem> roots, Guid id) => Walk(roots).FirstOrDefault(n => n.Item.Id == id);
    public static int Height(TaskItem item) => item.Subtasks.Count == 0 ? 0 : 1 + item.Subtasks.Max(Height);
    public static bool IsCompleted(TaskItem item) => item is Subtask child ? child.IsCompleted : item.CompletedAt.HasValue;
    public static bool CanPlace(IEnumerable<TaskItem> roots, TaskMove move)
    {
        if (!Enum.IsDefined(move.Position) || move.SourceId == move.TargetId) return false;
        var source = Find(roots, move.SourceId); var target = Find(roots, move.TargetId);
        if (source == null || target == null || Walk([source.Item]).Any(n => n.Item.Id == move.TargetId)) return false;
        int depth = target.Depth + (move.Position == TaskDropPosition.Inside ? 1 : 0);
        return depth + Height(source.Item) <= MaximumDepth;
    }
    // The same fields survive a root/child conversion, including unknown fields.
    public static TaskItem AsRoot(TaskItem item) => Copy(item, new TaskItem());
    public static Subtask AsChild(TaskItem item, bool completed) => Copy(item, new Subtask { IsCompleted = completed });
    private static T Copy<T>(TaskItem source, T target) where T : TaskItem
    {
        target.Id = source.Id; target.Title = source.Title; target.Description = source.Description; target.Activity = source.Activity;
        target.CreatedAt = source.CreatedAt; target.CompletedAt = source.CompletedAt; target.CompletionId = source.CompletionId;
        target.RoutineId = source.RoutineId; target.DueDate = source.DueDate; target.OccurrenceKey = source.OccurrenceKey;
        target.Repeats = source.Repeats; target.Subtasks = source.Subtasks; target.Extra = source.Extra; return target;
    }
}
