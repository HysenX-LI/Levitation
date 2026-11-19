namespace Levitation.Core;

public sealed class TaskService(DataStore store, Func<DateTimeOffset>? clock = null)
{
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.Now;
    public DateOnly Today => DateOnly.FromDateTime(Now.Date);
    public AppState State => store.State;
    public event Action? Changed;

    private void Commit(Action<AppState> change)
    {
        var next = store.Snapshot(); change(next); store.Save(next); Changed?.Invoke();
    }

    public IEnumerable<TaskItem> Pending => State.Tasks.Where(t => t.CompletedAt == null &&
        (!t.Repeats || t.RoutineId == null || t.DueDate == Today));
    public IEnumerable<TaskItem> CompletedToday => State.Tasks.Where(t => t.CompletedAt.HasValue &&
        DateOnly.FromDateTime(t.CompletedAt.Value.Date) == Today).OrderByDescending(t => t.CompletedAt);

    public void AddTask(string title)
    {
        title = title.Trim(); if (title.Length == 0) return;
        Commit(s => s.Tasks.Add(new TaskItem { Title = title, CreatedAt = Now }));
    }
    public void EditTask(Guid id, string title, string? description)
    {
        title = title.Trim(); if (title.Length == 0) return;
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Commit(s =>
        {
            var task = TaskTree.Find(s.Tasks, id)!.Item;
            task.Title = title; task.Description = description;
        });
    }
    public void Complete(Guid id, bool completed)
    {
        var task = State.Tasks.Single(t => t.Id == id);
        if (task.CompletedAt.HasValue == completed) return;
        Commit(s =>
        {
            var item = s.Tasks.Single(t => t.Id == id);
            item.Activity = TaskActivity.NotStarted;
            if (completed)
            {
                RecordCompletion(s, item, Now);
            }
            else
            {
                s.History.RemoveAll(h => h.Id == item.CompletionId);
                item.CompletionId = null; item.CompletedAt = null;
                // An older recurring item explicitly reopened by the user becomes a normal task.
                if (item.Repeats && item.DueDate != Today) { item.RoutineId = null; item.Repeats = false; }
            }
        });
    }
    private static void RecordCompletion(AppState state, TaskItem task, DateTimeOffset completedAt)
    {
        var record = new Completion { TaskId = task.Id, Title = task.Title, CompletedAt = completedAt,
            RoutineId = task.RoutineId, Subtasks = TaskTree.CaptureSubtasks(task) };
        state.History.Add(record);
        task.CompletionId = record.Id;
        task.CompletedAt = completedAt;
    }
    public void StartTask(Guid id) => ChangeActivity(id, TaskActivity.Running);
    public void PauseTask(Guid id) => ChangeActivity(id, TaskActivity.Paused);
    private void ChangeActivity(Guid id, TaskActivity activity)
    {
        var task = Pending.FirstOrDefault(t => t.Id == id);
        if (task == null || task.Activity == activity || (activity == TaskActivity.Paused && task.Activity != TaskActivity.Running)) return;
        Commit(s => s.Tasks.Single(t => t.Id == id).Activity = activity);
    }
    public Guid AddSubtask(Guid parentId, string title)
    {
        title = title.Trim();
        var parent = TaskTree.Find(Pending, parentId);
        if (title.Length == 0 || parent == null || parent.Depth >= TaskTree.MaximumDepth || TaskTree.IsCompleted(parent.Item)) return Guid.Empty;
        var child = new Subtask { Title = title, CreatedAt = Now };
        Commit(s => TaskTree.Find(s.Tasks, parentId)!.Item.Subtasks.Add(child)); return child.Id;
    }
    public void CompleteSubtask(Guid parentId, Guid childId, bool completed)
    {
        var child = TaskTree.Find(State.Tasks, parentId)?.Item.Subtasks.FirstOrDefault(c => c.Id == childId);
        if (child == null || child.IsCompleted == completed) return;
        DateTimeOffset? completedAt = completed ? Now : null;
        Commit(s =>
        {
            var parent = TaskTree.Find(s.Tasks, parentId)!;
            var item = parent.Item.Subtasks.Single(c => c.Id == childId);
            item.IsCompleted = completed;
            item.CompletedAt = completedAt;

            if (parent.Root.CompletionId is not Guid completionId) return;
            var record = s.History.FirstOrDefault(h => h.Id == completionId);
            if (record == null) return;
            record.Subtasks ??= TaskTree.CaptureSubtasks(parent.Root);
            int index = record.Subtasks.FindIndex(c => c.TaskId == childId);
            if (index >= 0) record.Subtasks[index] = record.Subtasks[index] with { IsCompleted = completed, CompletedAt = completedAt };
        });
    }
    public void EditSubtask(Guid parentId, Guid childId, string title)
    {
        title = title.Trim();
        var child = TaskTree.Find(State.Tasks, parentId)?.Item.Subtasks.FirstOrDefault(c => c.Id == childId);
        if (title.Length == 0 || child == null || child.Title == title) return;
        Commit(s => TaskTree.Find(s.Tasks, parentId)!.Item.Subtasks.Single(c => c.Id == childId).Title = title);
    }
    public void DeleteSubtask(Guid parentId, Guid childId)
    {
        if (TaskTree.Find(State.Tasks, parentId)?.Item.Subtasks.Any(c => c.Id == childId) != true) return;
        Commit(s =>
        {
            var parent = TaskTree.Find(s.Tasks, parentId)!.Item; int index = parent.Subtasks.FindIndex(c => c.Id == childId);
            var child = parent.Subtasks[index];
            // Keep the existing v1 Task envelope readable by older builds.
            // The optional metadata lets this build restore the parent link.
            PushTrash(s, new TrashItem { Task = TaskTree.AsRoot(child),
                ParentTaskId = parent.Id, ParentTitle = parent.Title, SubtaskWasCompleted = child.IsCompleted, OriginalIndex = index, DeletedAt = Now });
            SuppressOccurrences(s, child, true); parent.Subtasks.RemoveAt(index);
        });
    }
    public void DeleteTask(Guid id) => Commit(s =>
    {
        var index = s.Tasks.FindIndex(t => t.Id == id); if (index < 0) return;
        var task = s.Tasks[index];
        PushTrash(s, new TrashItem { Task = task, OriginalIndex = index, DeletedAt = Now });
        SuppressOccurrences(s, task, true);
        s.Tasks.RemoveAt(index);
    });
    public void SaveCard(Guid? id, string name, string content, bool expandSection = false)
    {
        content = content.Trim(); if (content.Length == 0) return;
        Commit(s =>
        {
            if (id.HasValue)
            {
                var card = s.Cards.Single(c => c.Id == id); card.Name = name.Trim(); card.Content = content;
            }
            else
            {
                s.Cards.Insert(0, new ContentCard { Name = name.Trim(), Content = content, CreatedAt = Now });
                if (expandSection) s.Preferences.CardsExpanded = true;
            }
        });
    }
    public void DeleteCard(Guid id) => Commit(s =>
    {
        var index = s.Cards.FindIndex(c => c.Id == id); if (index < 0) return;
        PushTrash(s, new TrashItem { Card = s.Cards[index], OriginalIndex = index, DeletedAt = Now });
        s.Cards.RemoveAt(index);
    });
    public int AddFileCards(IEnumerable<(string Path, bool IsFolder)> files)
    {
        var seen = State.Cards.Where(c => c.FilePath != null).Select(c => c.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<ContentCard>();
        foreach (var file in files)
        {
            string path = LocalFileLinks.Normalize(file.Path) ?? throw new ArgumentException("请选择有效的完整文件路径。");
            if (!seen.Add(path)) continue;
            additions.Add(new ContentCard { FilePath = path, IsFolder = file.IsFolder, Content = path, CreatedAt = Now });
        }
        if (additions.Count == 0) return 0;
        Commit(s => { s.Cards.InsertRange(0, additions); s.Preferences.CardsExpanded = true; }); return additions.Count;
    }
    public void UpdateFileCard(Guid id, string name, string path, bool isFolder)
    {
        path = LocalFileLinks.Normalize(path) ?? throw new ArgumentException("请选择有效的完整文件路径。");
        if (!State.Cards.Any(c => c.Id == id && c.IsFileReference)) return;
        if (State.Cards.Any(c => c.Id != id && string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("常用内容已经有这个文件入口。");
        Commit(s => { var card = s.Cards.Single(c => c.Id == id); card.Name = name.Trim(); card.FilePath = card.Content = path; card.IsFolder = isFolder; });
    }
    public void AddPastedImage(byte[] png)
    {
        var now = Now;
        var card = new ContentCard { Content = $"截图 {now:MM-dd HH:mm:ss}", CreatedAt = now, IsPastedImage = true };
        var next = store.Snapshot();
        next.Cards.Insert(0, card);
        next.Preferences.CardsExpanded = true;
        store.WritePastedImage(card.Id, png);
        try { store.Save(next); }
        catch
        {
            if (!ReferenceEquals(State, next)) store.DiscardPastedImage(card.Id);
            throw;
        }
        Changed?.Invoke();
    }

    public void RenamePastedImage(Guid id, string name)
    {
        name = name.Trim();
        if (!State.Cards.Any(c => c.Id == id && c.IsPastedImage && c.Name != name)) return;
        Commit(s => s.Cards.Single(c => c.Id == id).Name = name);
    }

    private static void PushTrash(AppState state, TrashItem entry)
    {
        state.Trash.Insert(0, entry);
        if (state.Trash.Count > 10) state.Trash.RemoveRange(10, state.Trash.Count - 10);
    }
    public void RestoreTrash(Guid id) => Commit(s =>
    {
        var entry = s.Trash.Single(t => t.Id == id);
        if (entry.Task is { } task && !TaskTree.Walk(s.Tasks).Any(n => TaskTree.Walk([task]).Any(c => c.Item.Id == n.Item.Id)))
        {
            if (entry.ParentTaskId is Guid parentId && TaskTree.Find(s.Tasks, parentId) is { } location &&
                location.Depth + 1 + TaskTree.Height(task) <= TaskTree.MaximumDepth)
            {
                var parent = location.Item;
                parent.Subtasks.Insert(Math.Clamp(entry.OriginalIndex, 0, parent.Subtasks.Count), TaskTree.AsChild(task, entry.SubtaskWasCompleted));
                SuppressOccurrences(s, task, false);
                s.Trash.Remove(entry); return;
            }
            if (task.CompletedAt.HasValue) task.Activity = TaskActivity.NotStarted;
            task.CompletedAt = null; task.CompletionId = null;
            SuppressOccurrences(s, task, false);
            if (task.DueDate != Today || !s.Routines.Any(r => r.Id == task.RoutineId)) { task.RoutineId = null; task.Repeats = false; }
            s.Tasks.Insert(entry.ParentTaskId.HasValue ? s.Tasks.Count : Math.Clamp(entry.OriginalIndex, 0, s.Tasks.Count), task);
        }
        if (entry.Card is { } card && !s.Cards.Any(c => c.Id == card.Id))
            s.Cards.Insert(Math.Clamp(entry.OriginalIndex, 0, s.Cards.Count), card);
        s.Trash.Remove(entry);
    });
    public void ClearTrash()
    {
        if (State.Trash.Count > 0) Commit(s => s.Trash.Clear());
    }
    public void SaveRoutine(Routine routine)
    {
        routine.Title = routine.Title.Trim();
        if (routine.Title.Length == 0) throw new ArgumentException("请输入任务内容。");
        if (routine.Frequency == RepeatFrequency.Weekly && routine.Weekdays.Count == 0) throw new ArgumentException("请选择至少一天。");
        Commit(s =>
        {
            int index = s.Routines.FindIndex(r => r.Id == routine.Id);
            if (index < 0) s.Routines.Add(routine); else s.Routines[index] = routine;
            foreach (var task in TaskTree.Walk(s.Tasks).Select(n => n.Item).Where(t => t.RoutineId == routine.Id && t.DueDate == Today && !TaskTree.IsCompleted(t)))
                task.Title = routine.Title;
            AddDueOccurrences(s);
        });
    }
    public void DeleteRoutine(Guid id) => Commit(s =>
    {
        s.Routines.RemoveAll(r => r.Id == id);
        foreach (var item in TaskTree.Walk(s.Tasks).Select(n => n.Item).Where(t => t.RoutineId == id && !TaskTree.IsCompleted(t)))
        { item.RoutineId = null; item.Repeats = false; }
    });
    public void EnsureToday()
    {
        var next = store.Snapshot();
        if (AddDueOccurrences(next) == 0) return;
        store.Save(next); Changed?.Invoke();
    }
    private int AddDueOccurrences(AppState state)
    {
        var due = state.Routines.Where(r => r.IsDue(Today)).Select(r => new
        {
            Rule = r, Date = r.Frequency == RepeatFrequency.Once ? r.StartDate : Today,
            Key = $"{r.Id:N}:{(r.Frequency == RepeatFrequency.Once ? r.StartDate : Today):yyyy-MM-dd}"
        }).Where(x => !TaskTree.Walk(state.Tasks).Any(n => n.Item.OccurrenceKey == x.Key) &&
                      !state.SuppressedOccurrences.Contains(x.Key) &&
                      !state.History.Any(h => h.TaskId == OccurrenceId(x.Key))).ToList();
        foreach (var x in due) state.Tasks.Add(new TaskItem
            {
                Id = OccurrenceId(x.Key), Title = x.Rule.Title, CreatedAt = Now, RoutineId = x.Rule.Id,
                DueDate = x.Date, OccurrenceKey = x.Key, Repeats = x.Rule.Frequency != RepeatFrequency.Once
            });
        return due.Count;
    }
    private static Guid OccurrenceId(string key) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    public void SavePreferences(Preferences preferences) => Commit(s => s.Preferences = preferences);
    public void SaveSectionExpanded(bool tasks, bool expanded)
    {
        if ((tasks ? State.Preferences.TasksExpanded : State.Preferences.CardsExpanded) == expanded) return;
        Commit(s => { if (tasks) s.Preferences.TasksExpanded = expanded; else s.Preferences.CardsExpanded = expanded; });
    }
    public void SaveSectionHeight(bool tasks, double height)
    {
        height = DockLayout.SectionHeight(tasks, height);
        Commit(s => { if (tasks) s.Preferences.TasksHeight = height; else s.Preferences.CardsHeight = height; });
    }
    public void ResetWindowSize()
    {
        var defaults = new Preferences();
        Commit(s =>
        {
            s.Preferences.Width = defaults.Width; s.Preferences.Height = defaults.Height;
            s.Preferences.TasksHeight = defaults.TasksHeight; s.Preferences.CardsHeight = defaults.CardsHeight;
        });
    }
    public void MoveTask(Guid id, Guid targetId, bool after)
    {
        if (id == targetId) return;
        var visible = Pending.Select(t => t.Id).ToHashSet();
        if (!visible.Contains(id) || !visible.Contains(targetId)) return;
        Commit(s =>
        {
            var task = s.Tasks.Single(t => t.Id == id); s.Tasks.Remove(task);
            int target = s.Tasks.FindIndex(t => t.Id == targetId);
            s.Tasks.Insert(target + (after ? 1 : 0), task);
        });
    }
    public bool CanMoveTask(TaskMove move)
    {
        if (!TaskTree.CanPlace(State.Tasks, move)) return false;
        var source = TaskTree.Find(State.Tasks, move.SourceId)!; var target = TaskTree.Find(State.Tasks, move.TargetId)!;
        var visible = Pending.Concat(CompletedToday).Select(t => t.Id).ToHashSet();
        // Completed root order is determined by its completion time, not dragging.
        if (!visible.Contains(source.Root.Id) || !visible.Contains(target.Root.Id) ||
            (source.Depth == 0 && source.Item.CompletedAt.HasValue) ||
            (move.Position != TaskDropPosition.Inside && target.Depth == 0 && target.Item.CompletedAt.HasValue)) return false;
        return true;
    }
    public void MoveTask(TaskMove move)
    {
        if (!CanMoveTask(move)) return;
        var source = TaskTree.Find(State.Tasks, move.SourceId)!; var target = TaskTree.Find(State.Tasks, move.TargetId)!;
        var destination = move.Position == TaskDropPosition.Inside ? target.Item : target.Parent;
        // Ignore dropping onto the same slot instead of writing a new revision.
        if (source.Parent?.Id == destination?.Id)
        {
            var siblings = source.Parent == null ? State.Tasks : source.Parent.Subtasks.Cast<TaskItem>().ToList();
            int from = siblings.FindIndex(n => n.Id == source.Item.Id);
            int to = move.Position == TaskDropPosition.Inside ? siblings.Count : siblings.FindIndex(n => n.Id == target.Item.Id) + (move.Position == TaskDropPosition.After ? 1 : 0);
            if (to == from || to == from + 1) return;
        }
        Commit(s =>
        {
            var item = TaskTree.Find(s.Tasks, move.SourceId)!; var anchor = TaskTree.Find(s.Tasks, move.TargetId)!;
            if (item.Parent == null) s.Tasks.Remove(item.Item); else item.Parent.Subtasks.Remove((Subtask)item.Item);
            var parent = move.Position == TaskDropPosition.Inside ? anchor.Item : anchor.Parent;
            if (parent != null)
            {
                int index = move.Position == TaskDropPosition.Inside ? parent.Subtasks.Count : parent.Subtasks.FindIndex(n => n.Id == anchor.Item.Id) + (move.Position == TaskDropPosition.After ? 1 : 0);
                parent.Subtasks.Insert(index, TaskTree.AsChild(item.Item, TaskTree.IsCompleted(item.Item)));
            }
            else
            {
                var root = TaskTree.AsRoot(item.Item);
                if (TaskTree.IsCompleted(item.Item))
                {
                    RecordCompletion(s, root, root.CompletedAt ?? Now);
                    root.Activity = TaskActivity.NotStarted;
                }
                if (!TaskTree.IsCompleted(item.Item)) { root.CompletedAt = null; root.CompletionId = null; }
                if (root.Repeats && root.DueDate != Today) { root.RoutineId = null; root.Repeats = false; }
                int index = s.Tasks.FindIndex(n => n.Id == anchor.Item.Id) + (move.Position == TaskDropPosition.After ? 1 : 0);
                s.Tasks.Insert(index, root);
            }
        });
    }
    private static void SuppressOccurrences(AppState state, TaskItem item, bool suppress)
    {
        foreach (var node in TaskTree.Walk([item]))
            if (node.Item.OccurrenceKey is { } key) { if (suppress) state.SuppressedOccurrences.Add(key); else state.SuppressedOccurrences.Remove(key); }
    }
    public void SaveWindowHeight(double height, double dockPosition, double floatingY) => SaveWindowSize(null, height, dockPosition, 0, floatingY);

    public void SaveWindowSize(double? width, double? height, double dockPosition, double floatingX, double floatingY)
    {
        if (width is double w && (!double.IsFinite(w) || w <= 0)) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is double h && (!double.IsFinite(h) || h <= 0)) throw new ArgumentOutOfRangeException(nameof(height));
        if (width == null && height == null) return;
        Commit(s =>
        {
            if (width.HasValue)
            {
                s.Preferences.Width = width.Value;
                s.Preferences.FloatingX = DockLayout.Normalize(floatingX);
            }
            if (height.HasValue)
            {
                s.Preferences.Height = height.Value;
                s.Preferences.DockPosition = DockLayout.Normalize(dockPosition);
                s.Preferences.FloatingY = DockLayout.Normalize(floatingY);
            }
        });
    }
    public void SaveWindowPlacement(bool docked, double position, double floatingX, double floatingY)
    {
        position = DockLayout.Normalize(position); floatingX = DockLayout.Normalize(floatingX); floatingY = DockLayout.Normalize(floatingY);
        var preferences = State.Preferences;
        if (preferences.IsDocked == docked && preferences.DockPosition == position && preferences.FloatingX == floatingX && preferences.FloatingY == floatingY) return;
        Commit(s => { s.Preferences.IsDocked = docked; s.Preferences.DockPosition = position; s.Preferences.FloatingX = floatingX; s.Preferences.FloatingY = floatingY; });
    }
    public int ImportLegacy(string directory)
    {
        int count = 0; Commit(s => count = LegacyImport.ReadInto(s, directory)); return count;
    }
    public void RestoreBackup(string path) { store.Restore(path, restored => AddDueOccurrences(restored)); Changed?.Invoke(); }
}
