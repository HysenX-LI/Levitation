using System.Text.Json;
using Levitation.Core;

var tests = new (string Name, Action Run)[]
{
    ("persist and reopen", Persist),
    ("exclusive writer", Exclusive),
    ("corrupt primary recovers and preserves evidence", Recover),
    ("unrecoverable data never becomes empty", Unrecoverable),
    ("newer schema is protected", FutureSchema),
    ("unknown compatible fields survive updates", UnknownFields),
    ("backup restoration preserves pre-restore data", Backup),
    ("save failure does not publish in-memory changes", FailedSave),
    ("legacy import is idempotent and leaves originals", Import),
    ("malformed legacy import is transactional", BadImport),
    ("daily and weekly occurrences survive restart", Recurring),
    ("trash retains last ten and restores named cards", Trash),
    ("clearing trash persists without changing active data or recurring suppression", ClearTrash),
    ("completion history survives task deletion", History),
    ("content names do not alter link targets", Links),
    ("incomplete valid JSON is not accepted as empty data", Incomplete),
    ("backup retention does not remove user exports", BackupRetention),
    ("missing primary recovers without reimporting legacy data", MissingPrimary),
    ("dock placement clamps at screen edges and remains stable across sizes", DockGeometry),
    ("only a deliberate rightward drag near the edge triggers docking", DockIntent),
    ("window mode and position survive restart without replacing other preferences", WindowPlacement),
    ("custom window height survives settings changes and restart", WindowHeight),
    ("window height respects small displays and invalid preferences", HeightBounds),
    ("auto hide resumes when transient interactions finish", AutoHide),
    ("right-edge hover gap stays open without extending across monitors", HoverTolerance),
    ("manual task order survives restart and preserves history", TaskOrder),
    ("start pause resume and completion preserve task semantics", TaskActivityTransitions),
    ("task activity survives restart ordering trash and backup restoration", TaskActivityPersistence),
    ("each recurring occurrence starts independently", RecurringTaskActivity),
    ("optional task descriptions survive completion trash backups and restart", TaskDescriptions),
    ("quick capture defaults on and its disabled setting survives updates", QuickCapturePreference),
    ("section visibility survives updates and defaults open", SectionVisibility),
    ("subtask changes stay independent of parent activity completion and history", SubtaskTransitions),
    ("subtasks survive reorder parent trash backups and restart", SubtaskPersistence),
    ("deleted subtasks restore in place or become normal tasks without their parent", SubtaskTrash),
    ("old data defaults to no subtasks and malformed children are rejected", SubtaskValidation),
    ("recurring task children belong only to their own occurrence", RecurringSubtasks),
    ("tree moves sort siblings move branches and promote children without losing identity", TreeMoves),
    ("tree moves reject cycles and whole branches exceeding three child levels", TreeDepthRules),
    ("tree moves preserve metadata completion states backups and restart", TreePersistence),
    ("nested trash restores whole branches with a safe fallback for changed depth", TreeTrash),
    ("nested recurring occurrences are neither duplicated nor regenerated after deletion", TreeRecurring),
    ("invalid tree data and failed tree writes never replace active data", TreeFailures),
    ("local file and image references stay separate from web links", FileReferences),
    ("file references survive rename relocation trash backups and restart", FileReferencePersistence),
    ("file batches validate completely and deduplicate paths without rewriting", FileReferenceBatches),
    ("file reference failures and corrupt metadata preserve active data", FileReferenceFailures),
    ("pasted screenshots survive restart, trash, and full backup on another profile", PastedImageBackup),
    ("pasted screenshots roll back when recording or restoring them fails", PastedImageRollback),
    ("reset window size preserves data and other preferences", ResetSize)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
static byte[] SamplePng() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNwcXH5DwADaAHMrIBaxAAAAABJRU5ErkJggg==");

static void PastedImageBackup()
{
    using var source = new TestDirectory();
    using var target = new TestDirectory();
    var png = SamplePng();
    var backup = Path.Combine(source.Path, "export.json");
    Guid imageId, trashedId;
    using (var store = new DataStore(source.Path))
    {
        var service = new TaskService(store);
        service.AddPastedImage(png);
        trashedId = store.State.Cards[0].Id;
        service.DeleteCard(trashedId);
        service.AddPastedImage(png);
        imageId = store.State.Cards[0].Id;
        service.RenamePastedImage(imageId, " 截图说明 ");
        service.AddTask("普通保存");
        Check(!File.ReadAllText(store.FilePath).Contains("pastedImages"));
        Check(File.ReadAllBytes(store.PastedImagePath(imageId)).SequenceEqual(png));
    }
    using (var reopened = new DataStore(source.Path))
    {
        Check(reopened.State.Cards[0] is { IsPastedImage: true, DisplayName: "截图说明", FilePath: null, Link: null });
        reopened.Export(backup);
    }
    using var restored = new DataStore(target.Path);
    var targetService = new TaskService(restored);
    targetService.RestoreBackup(backup);
    Check(restored.State.PastedImages == null && restored.State.Cards.Single().Id == imageId);
    Check(File.ReadAllBytes(restored.PastedImagePath(imageId)).SequenceEqual(png));
    var trashed = restored.State.Trash.Single();
    targetService.RestoreTrash(trashed.Id);
    Check(restored.State.Cards.Any(card => card.Id == trashedId && card.IsPastedImage));
    Check(File.ReadAllBytes(restored.PastedImagePath(trashedId)).SequenceEqual(png));
}

static void PastedImageRollback()
{
    using var source = new TestDirectory();
    using var target = new TestDirectory();
    using var store = new DataStore(source.Path);
    using var restored = new DataStore(target.Path);
    var service = new TaskService(store);
    var targetService = new TaskService(restored);
    Throws<InvalidDataException>(() => service.AddPastedImage([1, 2, 3]));
    Check(store.State.Cards.Count == 0);
    File.WriteAllText(restored.BackupsPath, "block backup creation");
    Throws<IOException>(() => targetService.AddPastedImage(SamplePng()));
    Check(restored.State.Cards.Count == 0 && !Directory.EnumerateFiles(Path.Combine(target.Path, "Images")).Any());
    service.AddPastedImage(SamplePng());
    var backup = Path.Combine(source.Path, "export.json");
    var id = store.State.Cards[0].Id;
    store.Export(backup);
    Throws<IOException>(() => targetService.RestoreBackup(backup));
    Check(restored.State.Cards.Count == 0 && !File.Exists(restored.PastedImagePath(id)));
}

static void FileReferences()
{
    var old = JsonSerializer.Deserialize<ContentCard>("{\"content\":\"https://example.com/a\"}", DataStore.Json)!;
    Check(!old.IsFileReference && old.Link!.Scheme == "https");
    Check(LocalFileLinks.Normalize(@"C:/资料/图片/截图 1.PNG") == @"C:\资料\图片\截图 1.PNG");
    Check(LocalFileLinks.Normalize(@"\\server\share\文档\") == @"\\server\share\文档");
    Check(LocalFileLinks.Normalize(@"C:\") == @"C:\");
    foreach (var bad in new[] { "relative.png", @"C:relative.png", "https://example.com/a.png", "javascript:alert(1)", @"C:\图片\..\命令.cmd", @"\\?\C:\file", @"C:\file.txt:stream", "C:\\bad\0.png" }) Check(LocalFileLinks.Normalize(bad) == null, bad);
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    Check(service.AddFileCards([(@"C:\资料\截图 1.PNG", false), (@"D:\项目资料", true)]) == 2);
    Check(store.State.Cards[0] is { DisplayName: "截图 1.PNG", IsFileReference: true, Link: null });
    Check(LocalFileLinks.IsImage(store.State.Cards[0].FilePath!) && !LocalFileLinks.IsImage(@"C:\资料\报告.pdf"));
    Check(store.State.Cards[1] is { IsFolder: true, DisplayName: "项目资料" });
    service.SaveCard(null, "网址", "https://example.com/path"); Check(store.State.Cards[0].Link!.Scheme == "https");
}
static void FileReferencePersistence()
{
    using var dir = new TestDirectory(); Guid id; var backup = Path.Combine(dir.Path, "files.json");
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddFileCards([(@"C:\资料\草图.png", false)]); id = store.State.Cards.Single().Id;
        service.UpdateFileCard(id, "界面草图", @"D:\新文件夹\新草图.jpg", false);
        service.DeleteCard(id); Check(store.State.Trash.Single().Card!.FilePath == @"D:\新文件夹\新草图.jpg");
        service.RestoreTrash(store.State.Trash.Single().Id); store.Export(backup);
        service.UpdateFileCard(id, "临时名称", @"E:\其他图.png", false); service.RestoreBackup(backup);
    }
    using var reopened = new DataStore(dir.Path); var card = reopened.State.Cards.Single();
    Check(card.Id == id && card is { Name: "界面草图", IsFolder: false, IsFileReference: true, Link: null });
    Check(card.FilePath == @"D:\新文件夹\新草图.jpg" && card.Content == card.FilePath);
}
static void FileReferenceBatches()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddFileCards([(@"C:\a & b.png", false)]); long revision = store.State.Revision;
    Check(service.AddFileCards([(@"c:/a & b.png", false)]) == 0 && store.State.Revision == revision);
    Throws<ArgumentException>(() => service.AddFileCards([(@"C:\valid.png", false), ("https://example.com/no.png", false)]));
    Check(store.State.Cards.Count == 1 && store.State.Revision == revision);
    Check(service.AddFileCards([(@"C:\资料", true), (@"C:\资料\", true), (@"C:\报告.pdf", false)]) == 2);
    var folder = store.State.Cards.Single(c => c.IsFolder);
    Throws<ArgumentException>(() => service.UpdateFileCard(folder.Id, "重复", @"C:\报告.pdf", false));
    Check(store.State.Cards.Count == 3 && store.State.Cards.Single(c => c.Id == folder.Id).IsFolder);
}
static void FileReferenceFailures()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddFileCards([(@"C:\有空格的路径\图片.png", false)]);
    var invalid = store.Snapshot(); invalid.Cards[0].FilePath = "javascript:alert(1)"; Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Cards[0].Content = "other content"; Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    string before = File.ReadAllText(store.FilePath); long revision = store.State.Revision;
    Directory.Delete(store.BackupsPath, true); File.WriteAllText(store.BackupsPath, "block backup creation");
    Throws<IOException>(() => service.AddFileCards([(@"C:\不得假装保存.png", false)]));
    Check(store.State.Cards.Count == 1 && store.State.Revision == revision && File.ReadAllText(store.FilePath) == before);
}
static void TreeMoves()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("A"); service.AddTask("B"); var a = service.Pending.First().Id; var b = service.Pending.Last().Id;
    var one = service.AddSubtask(a, "1"); var two = service.AddSubtask(a, "2"); var three = service.AddSubtask(a, "3");
    var leaf = service.AddSubtask(two, "2.1"); service.CompleteSubtask(two, leaf, true);
    var inventory = TaskTree.Walk(store.State.Tasks).Select(n => n.Item.Id).Order().ToArray();
    service.MoveTask(new(two, one, TaskDropPosition.Before));
    Check(TaskTree.Find(store.State.Tasks, a)!.Item.Subtasks.Select(n => n.Id).SequenceEqual(new[] { two, one, three }));
    service.MoveTask(new(two, b, TaskDropPosition.Inside));
    Check(TaskTree.Find(store.State.Tasks, two) is { Parent.Id: var parent, Depth: 1 } && parent == b);
    Check(((Subtask)TaskTree.Find(store.State.Tasks, leaf)!.Item).IsCompleted);
    service.MoveTask(new(one, two, TaskDropPosition.After));
    Check(TaskTree.Find(store.State.Tasks, b)!.Item.Subtasks.Select(n => n.Id).SequenceEqual(new[] { two, one }));
    service.MoveTask(new(three, b, TaskDropPosition.Before));
    Check(service.Pending.Select(n => n.Id).SequenceEqual(new[] { a, three, b }));
    Check(TaskTree.Walk(store.State.Tasks).Select(n => n.Item.Id).Order().SequenceEqual(inventory));
    Check(store.State.History.Count == 0);
    long revision = store.State.Revision; service.MoveTask(new(two, one, TaskDropPosition.Before));
    Check(store.State.Revision == revision, "same sibling slot rewrote the file");
}
static void TreeDepthRules()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("A"); service.AddTask("B"); var a = service.Pending.First().Id; var b = service.Pending.Last().Id;
    var a1 = service.AddSubtask(a, "A1"); var a2 = service.AddSubtask(a1, "A2"); var a3 = service.AddSubtask(a2, "A3");
    var b1 = service.AddSubtask(b, "B1"); Check(a3 != Guid.Empty && b1 != Guid.Empty);
    long revision = store.State.Revision;
    Check(service.AddSubtask(a3, "第四层") == Guid.Empty);
    foreach (var move in new[] { new TaskMove(a, b, TaskDropPosition.Inside), new(a1, b1, TaskDropPosition.Inside), new(a, a3, TaskDropPosition.Before), new(a1, a3, TaskDropPosition.Inside), new(a2, a2, TaskDropPosition.After), new(a3, b, (TaskDropPosition)99) })
    {
        Check(!service.CanMoveTask(move)); service.MoveTask(move);
    }
    Check(store.State.Revision == revision && TaskTree.Find(store.State.Tasks, a3)!.Depth == 3);
    var legal = new TaskMove(a1, b, TaskDropPosition.Inside); Check(service.CanMoveTask(legal)); service.MoveTask(legal);
    Check(TaskTree.Find(store.State.Tasks, a3) is { Depth: 3, Root.Id: var root } && root == b);
    Check(TaskTree.Find(store.State.Tasks, a)!.Item.Subtasks.Count == 0);
    service.MoveTask(new(a3, a1, TaskDropPosition.Before)); Check(TaskTree.Find(store.State.Tasks, a3)!.Depth == 1);
    Check(service.AddSubtask(a3, "可再添加") != Guid.Empty);
}
static void TreePersistence()
{
    using var dir = new TestDirectory(); Guid a, b, child, leaf; var backup = Path.Combine(dir.Path, "tree.json");
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("带说明的根节点"); a = service.Pending.Single().Id;
        service.EditTask(a, "带说明的根节点", "中文说明 Agypj"); service.StartTask(a);
        service.AddTask("新父项"); b = service.Pending.Last().Id; child = service.AddSubtask(a, "子项"); leaf = service.AddSubtask(child, "孙项");
        service.CompleteSubtask(child, leaf, true);
        var snapshot = store.Snapshot(); snapshot.Tasks.Single(n => n.Id == a).Extra = new() { ["futureData"] = JsonSerializer.SerializeToElement(42) }; store.Save(snapshot);
        service.MoveTask(new(a, b, TaskDropPosition.Inside));
        Check(TaskTree.Find(store.State.Tasks, a)!.Item is Subtask { Activity: TaskActivity.Running, Description: "中文说明 Agypj", IsCompleted: false });
        Check(TaskTree.Find(store.State.Tasks, leaf)!.Depth == 3);
        store.Export(backup); service.MoveTask(new(a, b, TaskDropPosition.Before));
        Check(service.Pending.First() is { Activity: TaskActivity.Running, Description: "中文说明 Agypj" });
        service.RestoreBackup(backup);
    }
    using var reopened = new DataStore(dir.Path); var reopenedService = new TaskService(reopened);
    Check(TaskTree.Find(reopened.State.Tasks, a)!.Item.Extra!["futureData"].GetInt32() == 42);
    Check(TaskTree.Find(reopened.State.Tasks, leaf)!.Item is Subtask { IsCompleted: true });
    reopenedService.MoveTask(new(leaf, b, TaskDropPosition.Before));
    Check(reopenedService.CompletedToday.Single().Id == leaf && reopened.State.History.Single().TaskId == leaf);
    Check(TaskTree.Find(reopened.State.Tasks, child)!.Item.Subtasks.Count == 0);
    reopenedService.MoveTask(new(a, b, TaskDropPosition.Before));
    Check(reopenedService.Pending.First().Id == a && reopenedService.Pending.First().Activity == TaskActivity.Running);
}
static void TreeTrash()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("A"); service.AddTask("X"); service.AddTask("Y"); var ids = service.Pending.Select(n => n.Id).ToArray();
    var b = service.AddSubtask(ids[0], "B"); var c = service.AddSubtask(b, "C"); var d = service.AddSubtask(c, "D"); service.CompleteSubtask(c, d, true);
    service.DeleteSubtask(b, c); service.RestoreTrash(store.State.Trash.Single().Id);
    Check(TaskTree.Find(store.State.Tasks, d) is { Depth: 3, Item: Subtask { IsCompleted: true } });
    service.DeleteSubtask(ids[0], b); var deleted = store.State.Trash.Single().Id;
    service.MoveTask(new(ids[1], ids[2], TaskDropPosition.Inside)); service.MoveTask(new(ids[0], ids[1], TaskDropPosition.Inside));
    service.RestoreTrash(deleted); // Original parent is now too deep for the branch.
    Check(TaskTree.Find(store.State.Tasks, b) is { Depth: 0, Parent: null });
    Check(TaskTree.Find(store.State.Tasks, d) is { Depth: 2, Item: Subtask { IsCompleted: true } });
    service.DeleteTask(ids[2]); service.RestoreTrash(store.State.Trash.Single().Id);
    Check(TaskTree.Find(store.State.Tasks, ids[0])!.Depth == 2 && store.State.Trash.Count == 0);
}
static void TreeRecurring()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path);
    var now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(8)); var service = new TaskService(store, () => now);
    service.AddTask("项目"); var parent = service.Pending.Single().Id;
    var routine = new Routine { Title = "每日事项", Frequency = RepeatFrequency.Daily, StartDate = service.Today }; service.SaveRoutine(routine);
    var occurrence = service.Pending.Last().Id; service.MoveTask(new(occurrence, parent, TaskDropPosition.Inside));
    long revision = store.State.Revision; service.EnsureToday(); Check(store.State.Revision == revision && store.State.Tasks.Count == 1);
    service.DeleteSubtask(parent, occurrence); service.EnsureToday(); Check(store.State.Tasks.Count == 1 && store.State.Tasks[0].Subtasks.Count == 0);
    service.RestoreTrash(store.State.Trash.Single().Id); now = now.AddDays(1); service.EnsureToday();
    Check(store.State.Tasks.Count == 2 && TaskTree.Find(store.State.Tasks, occurrence)!.Depth == 1);
    service.DeleteRoutine(routine.Id); Check(TaskTree.Walk(store.State.Tasks).All(n => n.Item.RoutineId == null && !n.Item.Repeats));
}
static void TreeFailures()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("A"); service.AddTask("B"); var a = service.Pending.First().Id; var b = service.Pending.Last().Id;
    var child = service.AddSubtask(a, "一层"); var grandchild = service.AddSubtask(child, "二层"); var leaf = service.AddSubtask(grandchild, "三层");
    var invalid = store.Snapshot(); invalid.Tasks[0].Subtasks[0].Subtasks[0].Subtasks[0].Subtasks.Add(new Subtask { Title = "第四层" });
    Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Tasks[1].Subtasks.Add(new Subtask { Id = child, Title = "跨树重复ID" }); Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Tasks[0].Subtasks[0].Subtasks[0].Subtasks = null!; Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    var before = File.ReadAllText(store.FilePath); long revision = store.State.Revision;
    Directory.Delete(store.BackupsPath, true); File.WriteAllText(store.BackupsPath, "block backup directory creation");
    Throws<IOException>(() => service.MoveTask(new(child, b, TaskDropPosition.Inside)));
    Check(store.State.Revision == revision && TaskTree.Find(store.State.Tasks, leaf)!.Root.Id == a && File.ReadAllText(store.FilePath) == before);
}
static void SubtaskTransitions()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("父待办"); var parent = service.Pending.Single().Id;
    service.AddTask("另一条"); var order = service.Pending.Select(t => t.Id).ToArray(); service.StartTask(parent);
    var first = service.AddSubtask(parent, "  写大纲  "); var second = service.AddSubtask(parent, "找资料");
    Check(first != Guid.Empty && first != second);
    service.EditSubtask(parent, second, "  补充资料  "); service.CompleteSubtask(parent, first, true); service.CompleteSubtask(parent, second, true);
    var task = store.State.Tasks.Single(t => t.Id == parent);
    Check(task.Subtasks.Select(c => c.Title).SequenceEqual(new[] { "写大纲", "补充资料" }));
    Check(task.CompletedAt == null && task.Activity == TaskActivity.Running && store.State.History.Count == 0);
    Check(service.Pending.Select(t => t.Id).SequenceEqual(order));
    service.CompleteSubtask(parent, first, false); service.PauseTask(parent); service.Complete(parent, true);
    Check(store.State.Tasks.Single(t => t.Id == parent).Subtasks.Select(c => c.IsCompleted).SequenceEqual(new[] { false, true }));
    Check(store.State.History.Count == 1);
    long revision = store.State.Revision; Check(service.AddSubtask(parent, "不能添加到已完成项") == Guid.Empty);
    service.EditSubtask(parent, second, " "); service.CompleteSubtask(parent, second, true); service.DeleteSubtask(parent, Guid.NewGuid());
    Check(store.State.Revision == revision);
    service.CompleteSubtask(parent, first, true); Check(store.State.History.Count == 1);
    service.Complete(parent, false);
    Check(store.State.Tasks.Single(t => t.Id == parent) is { Activity: TaskActivity.NotStarted, CompletedAt: null });
    Check(store.State.Tasks.Single(t => t.Id == parent).Subtasks.All(c => c.IsCompleted) && store.State.History.Count == 0);
}
static void SubtaskPersistence()
{
    using var dir = new TestDirectory(); Guid parent, first, second; var backup = Path.Combine(dir.Path, "children.json");
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("有子项"); parent = service.Pending.Single().Id;
        service.AddTask("另一项"); var other = service.Pending.Last().Id;
        first = service.AddSubtask(parent, "第一步"); second = service.AddSubtask(parent, "第二步"); service.CompleteSubtask(parent, second, true);
        var snapshot = store.Snapshot(); snapshot.Tasks.Single(t => t.Id == parent).Subtasks[0].Extra = new() { ["futureField"] = JsonSerializer.SerializeToElement("保留") }; store.Save(snapshot);
        service.MoveTask(parent, other, true); Check(service.Pending.Last().Id == parent);
        service.Complete(parent, true); service.DeleteTask(parent);
        Check(store.State.Trash.Single().Task!.Subtasks.Count == 2);
        service.RestoreTrash(store.State.Trash.Single().Id); store.Export(backup);
        service.EditSubtask(parent, first, "临时修改"); service.DeleteSubtask(parent, second); service.RestoreBackup(backup);
    }
    using var reopened = new DataStore(dir.Path); var task = reopened.State.Tasks.Single(t => t.Id == parent);
    Check(task.Subtasks.Select(c => c.Id).SequenceEqual(new[] { first, second }));
    Check(task.Subtasks[0].Title == "第一步" && !task.Subtasks[0].IsCompleted && task.Subtasks[1].IsCompleted);
    Check(task.Subtasks[0].Extra!["futureField"].GetString() == "保留");
    Check(task.CompletedAt == null && reopened.State.History.Count == 1 && reopened.State.Trash.Count == 0);
    Check(reopened.State.Tasks.Last().Id == parent);
}
static void SubtaskTrash()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("父项"); var parent = service.Pending.Single().Id;
    var first = service.AddSubtask(parent, "一"); var second = service.AddSubtask(parent, "二"); var third = service.AddSubtask(parent, "三");
    service.CompleteSubtask(parent, second, true); service.DeleteSubtask(parent, second);
    Check(store.State.Trash.Single() is { Kind: "子待办", ParentTitle: "父项", SubtaskWasCompleted: true });
    service.RestoreTrash(store.State.Trash.Single().Id);
    Check(store.State.Tasks.Single().Subtasks.Select(c => c.Id).SequenceEqual(new[] { first, second, third }));
    Check(store.State.Tasks.Single().Subtasks[1].IsCompleted && store.State.History.Count == 0);
    service.DeleteSubtask(parent, second); var childTrash = store.State.Trash.Single().Id;
    service.DeleteTask(parent); service.RestoreTrash(childTrash);
    Check(service.Pending.Single() is { Id: var id, Title: "二", CompletedAt: null } && id == second);
    service.RestoreTrash(store.State.Trash.Single().Id);
    Check(store.State.Tasks.Single(t => t.Id == parent).Subtasks.Select(c => c.Id).SequenceEqual(new[] { first, third }));
    Check(store.State.Tasks.Count == 2 && store.State.Trash.Count == 0);
    for (int i = 0; i < 12; i++) service.DeleteSubtask(parent, service.AddSubtask(parent, "删除" + i));
    Check(store.State.Trash.Count == 10 && store.State.Trash.All(t => t.Kind == "子待办"));
}
static void SubtaskValidation()
{
    Check(JsonSerializer.Deserialize<TaskItem>("{\"title\":\"旧待办\"}", DataStore.Json)!.Subtasks.Count == 0);
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("父项"); var parent = service.Pending.Single().Id;
    long revision = store.State.Revision; Check(service.AddSubtask(parent, " ") == Guid.Empty && service.AddSubtask(Guid.NewGuid(), "丢失父项") == Guid.Empty);
    Check(store.State.Revision == revision);
    var child = service.AddSubtask(parent, "有效");
    var invalid = store.Snapshot(); invalid.Tasks[0].Subtasks = null!; Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Tasks[0].Subtasks.Add(new Subtask { Id = child, Title = "重复ID" }); Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Tasks[0].Subtasks.Add(null!); Throws<InvalidDataException>(() => DataStore.Validate(invalid));
    invalid = store.Snapshot(); invalid.Tasks[0].Subtasks[0].Id = Guid.Empty; Throws<InvalidDataException>(() => DataStore.Validate(invalid));
}
static void RecurringSubtasks()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path);
    var now = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(8)); var service = new TaskService(store, () => now);
    service.SaveRoutine(new Routine { Title = "每日任务", Frequency = RepeatFrequency.Daily, StartDate = service.Today });
    var yesterday = service.Pending.Single().Id; var child = service.AddSubtask(yesterday, "仅今天"); service.CompleteSubtask(yesterday, child, true);
    now = now.AddDays(1); service.EnsureToday();
    Check(service.Pending.Single().Id != yesterday && service.Pending.Single().Subtasks.Count == 0);
    Check(store.State.Tasks.Single(t => t.Id == yesterday).Subtasks.Single() is { Id: var id, IsCompleted: true } && id == child);
}
static void TaskActivityTransitions()
{
    Check(JsonSerializer.Deserialize<TaskItem>("{\"title\":\"旧待办\"}", DataStore.Json)!.Activity == TaskActivity.NotStarted);
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("开始与暂停"); service.AddTask("另一件事");
    var first = service.Pending.First().Id; var second = service.Pending.Last().Id;
    var ids = service.Pending.Select(t => t.Id).ToArray(); long revision = store.State.Revision;
    service.PauseTask(first); service.StartTask(Guid.NewGuid());
    Check(store.State.Revision == revision, "invalid transitions wrote data");
    service.StartTask(first); service.StartTask(second);
    Check(service.Pending.All(t => t.Activity == TaskActivity.Running), "starting a second item stopped the first");
    revision = store.State.Revision; service.StartTask(first); Check(store.State.Revision == revision);
    service.PauseTask(first); Check(store.State.Tasks.Single(t => t.Id == first).Activity == TaskActivity.Paused);
    revision = store.State.Revision; service.PauseTask(first); Check(store.State.Revision == revision);
    service.StartTask(first); Check(store.State.Tasks.Single(t => t.Id == first).Activity == TaskActivity.Running);
    Check(store.State.History.Count == 0 && service.Pending.Select(t => t.Id).SequenceEqual(ids));
    service.Complete(first, true); service.Complete(first, true);
    Check(service.CompletedToday.Single().Activity == TaskActivity.NotStarted && store.State.History.Count == 1);
    revision = store.State.Revision; service.StartTask(first); service.PauseTask(first); Check(store.State.Revision == revision);
    service.Complete(first, false);
    Check(store.State.Tasks.Single(t => t.Id == first).Activity == TaskActivity.NotStarted && store.State.History.Count == 0);
    service.PauseTask(second); service.Complete(second, true); service.Complete(second, false);
    Check(store.State.Tasks.Single(t => t.Id == second).Activity == TaskActivity.NotStarted);
    var invalid = store.Snapshot(); invalid.Tasks[0].Activity = (TaskActivity)99;
    Throws<InvalidDataException>(() => DataStore.Validate(invalid));
}
static void TaskActivityPersistence()
{
    using var dir = new TestDirectory(); Guid running, paused;
    var backup = Path.Combine(dir.Path, "activity-backup.json");
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("正在做"); running = service.Pending.Single().Id;
        service.AddTask("暂停中"); paused = service.Pending.Last().Id;
        service.EditTask(running, "正在做", "说明保持不变");
        service.StartTask(running); service.StartTask(paused); service.PauseTask(paused);
        service.MoveTask(paused, running, false);
        service.DeleteTask(running); Check(store.State.Trash.Single().Task!.Activity == TaskActivity.Running);
        service.RestoreTrash(store.State.Trash.Single().Id); store.Export(backup);
    }
    using var reopened = new DataStore(dir.Path); var reopenedService = new TaskService(reopened);
    Check(reopened.State.Tasks.Single(t => t.Id == running) is { Activity: TaskActivity.Running, Description: "说明保持不变" });
    Check(reopened.State.Tasks.Single(t => t.Id == paused).Activity == TaskActivity.Paused);
    Check(reopenedService.Pending.Select(t => t.Id).SequenceEqual(new[] { paused, running }));
    reopenedService.StartTask(paused); reopenedService.PauseTask(running); reopenedService.RestoreBackup(backup);
    Check(reopened.State.Tasks.Single(t => t.Id == running).Activity == TaskActivity.Running);
    Check(reopened.State.Tasks.Single(t => t.Id == paused).Activity == TaskActivity.Paused);
    Check(reopened.State.History.Count == 0);
}
static void RecurringTaskActivity()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path);
    var now = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(8));
    var service = new TaskService(store, () => now);
    service.SaveRoutine(new Routine { Title = "每天重新开始", Frequency = RepeatFrequency.Daily, StartDate = service.Today });
    Guid yesterday = service.Pending.Single().Id; service.StartTask(yesterday);
    now = now.AddDays(1); service.EnsureToday();
    var today = service.Pending.Single(); Check(today.Id != yesterday && today.Activity == TaskActivity.NotStarted);
    long revision = store.State.Revision; service.StartTask(yesterday); service.PauseTask(yesterday); Check(store.State.Revision == revision);
    service.StartTask(today.Id); service.PauseTask(today.Id);
    Check(store.State.Tasks.Single(t => t.Id == yesterday).Activity == TaskActivity.Running);
    Check(service.Pending.Single().Activity == TaskActivity.Paused);
}
static void HoverTolerance()
{
    // The panel ends 12 logical pixels before the right edge. The edge entry
    // reappears there on collapse, so treating that gap as outside caused a loop.
    foreach (double scale in new[] { 1d, 1.25, 1.5, 2, 2.5 })
    {
        double workWidth = 1920 / scale, workHeight = 1080 / scale;
        double left = workWidth - 352, top = 24, width = 340, height = workHeight - 48;
        for (double x = left + width; x < workWidth; x += 1 / scale)
            Check(DockLayout.PointerKeepsPanelOpen(x, top + height / 2, left, top, width, height, workWidth, workHeight, true), "right margin triggered auto hide");
        Check(DockLayout.PointerKeepsPanelOpen(left - 7, top - 15, left, top, width, height, workWidth, workHeight, true));
        Check(!DockLayout.PointerKeepsPanelOpen(left - 9, top + 100, left, top, width, height, workWidth, workHeight, true));
        Check(!DockLayout.PointerKeepsPanelOpen(workWidth - 1 / scale, top - 17, left, top, width, height, workWidth, workHeight, true));
        Check(!DockLayout.PointerKeepsPanelOpen(workWidth - 1 / scale, top + height + 17, left, top, width, height, workWidth, workHeight, true));
        Check(!DockLayout.PointerKeepsPanelOpen(workWidth, top + 100, left, top, width, height, workWidth, workHeight, true), "neighboring monitor kept the panel open");
        Check(!DockLayout.PointerKeepsPanelOpen(-1, top + 100, left, top, width, height, workWidth, workHeight, true));
        Check(!DockLayout.PointerKeepsPanelOpen(left + 100, workHeight, left, top, width, height, workWidth, workHeight, true));
    }
    Check(DockLayout.PointerKeepsPanelOpen(750, 250, 400, 200, 340, 600, 1920, 1080, false));
    Check(!DockLayout.PointerKeepsPanelOpen(765, 250, 400, 200, 340, 600, 1920, 1080, false));
    Check(!DockLayout.PointerKeepsPanelOpen(1919, 250, 400, 200, 340, 600, 1920, 1080, false), "floating window acquired a desktop-wide hover area");
}
static void TaskDescriptions()
{
    Check(JsonSerializer.Deserialize<TaskItem>("{\"title\":\"旧待办\"}", DataStore.Json)!.Description == null);
    using var dir = new TestDirectory(); Guid id;
    const string description = "补齐上周的截图\n整理会议记录 Agypj";
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("整理资料"); id = service.Pending.Single().Id;
        service.AddTask("没有说明");
        service.EditTask(id, "整理项目资料", "  " + description + "\n ");
        service.Complete(id, true); Check(service.CompletedToday.Single().Description == description);
        service.DeleteTask(id); Check(store.State.Trash.Single().Task!.Description == description);
        service.RestoreTrash(store.State.Trash.Single().Id); Check(service.Pending.Single(t => t.Id == id).Description == description);
        string backup = Path.Combine(dir.Path, "with-description.json"); store.Export(backup);
        service.EditTask(id, "临时修改", null); service.RestoreBackup(backup);
    }
    using var reopened = new DataStore(dir.Path); var reopenedService = new TaskService(reopened);
    Check(reopened.State.Tasks.Single(t => t.Id == id) is { Title: "整理项目资料", Description: description });
    Check(reopened.State.Tasks.Single(t => t.Id != id).Description == null);
    reopenedService.EditTask(id, "保留标题", " \n\t ");
    Check(DataStore.ReadFile(reopened.FilePath).Tasks.Single(t => t.Id == id) is { Title: "保留标题", Description: null });
}
static void QuickCapturePreference()
{
    Check(JsonSerializer.Deserialize<Preferences>("{}", DataStore.Json)!.QuickCaptureEnabled, "old preferences did not receive the default shortcut");
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); var preferences = store.Snapshot().Preferences;
        preferences.QuickCaptureEnabled = false; service.SavePreferences(preferences);
        service.AddTask("快速记录：中文 Agypj"); service.SaveSectionExpanded(true, false); service.ResetWindowSize();
    }
    using var reopened = new DataStore(dir.Path);
    Check(!reopened.State.Preferences.QuickCaptureEnabled);
    Check(reopened.State.Tasks.Single().Title == "快速记录：中文 Agypj");
}
static void TaskOrder()
{
    using var dir = new TestDirectory(); Guid[] ids;
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store);
        foreach (var title in new[] { "A", "B", "C", "D" }) service.AddTask(title);
        ids = service.Pending.Select(t => t.Id).ToArray();
        service.Complete(ids[1], true); var history = JsonSerializer.Serialize(store.State.History);
        service.MoveTask(ids[3], ids[0], false);
        Check(string.Join("", service.Pending.Select(t => t.Title)) == "DAC");
        service.MoveTask(ids[3], ids[2], true);
        Check(string.Join("", service.Pending.Select(t => t.Title)) == "ACD");
        service.MoveTask(ids[0], ids[3], true);
        Check(string.Join("", service.Pending.Select(t => t.Title)) == "CDA");
        long revision = store.State.Revision;
        service.MoveTask(ids[2], ids[2], false); service.MoveTask(Guid.NewGuid(), ids[0], true);
        service.MoveTask(ids[1], ids[0], false); service.MoveTask(ids[0], ids[1], true);
        Check(store.State.Revision == revision, "invalid or completed drag changed the data");
        Check(JsonSerializer.Serialize(store.State.History) == history);
        service.SaveSectionExpanded(true, false); service.SaveWindowHeight(480, .5, .5);
    }
    using var reopened = new DataStore(dir.Path);
    Check(string.Join("", new TaskService(reopened).Pending.Select(t => t.Title)) == "CDA");
    Check(reopened.State.Tasks.Select(t => t.Id).Order().SequenceEqual(ids.Order()));
    Check(reopened.State.History.Single().Title == "B");
}
static void SectionVisibility()
{
    Check(JsonSerializer.Deserialize<Preferences>("{}", DataStore.Json) is { TasksExpanded: true, CardsExpanded: true });
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("折叠不会删除"); service.SaveCard(null, "卡片", "保留内容");
        service.SaveSectionExpanded(true, false); service.SaveSectionExpanded(false, false);
        service.SaveSectionHeight(true, -10); service.SaveSectionHeight(false, 20);
        Check(store.State.Preferences.TasksHeight == DockLayout.TaskSectionMinimum && store.State.Preferences.CardsHeight == DockLayout.CardSectionMinimum);
        service.SaveSectionHeight(true, 410); service.SaveSectionHeight(false, 260);
        service.SaveWindowPlacement(true, .7, .5, .5); service.SaveWindowHeight(520, .7, .5);
        var settings = store.Snapshot().Preferences; settings.Theme = "Dark"; service.SavePreferences(settings);
    }
    using var reopened = new DataStore(dir.Path);
    Check(!reopened.State.Preferences.TasksExpanded && !reopened.State.Preferences.CardsExpanded);
    Check(reopened.State.Preferences.TasksHeight == 410 && reopened.State.Preferences.CardsHeight == 260);
    Check(DockLayout.SectionHeight(true, double.NaN) == 320 && DockLayout.SectionHeight(false, double.PositiveInfinity) == 220);
    Check(reopened.State.Tasks.Single().Title == "折叠不会删除" && reopened.State.Cards.Single().Content == "保留内容");
    new TaskService(reopened).SaveSectionExpanded(true, true);
    Check(reopened.State.Preferences.TasksExpanded && !reopened.State.Preferences.CardsExpanded);
}
static void ResetSize()
{
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("恢复尺寸后保留"); service.SaveCard(null, "卡片", "原文");
        var preferences = store.Snapshot().Preferences;
        preferences.Theme = "Dark"; preferences.AutoHideSeconds = 2; preferences.Width = 390;
        preferences.Height = 500; preferences.TasksHeight = 440; preferences.CardsHeight = 280;
        preferences.DockPosition = .8; preferences.CardsExpanded = false;
        service.SavePreferences(preferences); service.ResetWindowSize();
        var expected = new Preferences(); var actual = store.State.Preferences;
        Check(actual.Width == expected.Width && actual.Height == null && actual.TasksHeight == expected.TasksHeight && actual.CardsHeight == expected.CardsHeight);
        Check(actual.Theme == "Dark" && actual.AutoHideSeconds == 2 && actual.DockPosition == .8 && !actual.CardsExpanded);
    }
    using var reopened = new DataStore(dir.Path);
    Check(reopened.State.Preferences.Height == null && reopened.State.Preferences.TasksHeight == 320);
    Check(reopened.State.Tasks.Single().Title == "恢复尺寸后保留" && reopened.State.Cards.Single().Content == "原文");
}
static void DockGeometry()
{
    foreach (double height in new[] { 600d, 900d, 1440d })
    {
        double panel = Math.Min(860, height * .78), previous = -1;
        var top = DockLayout.Calculate(height, panel, 106, 0); var bottom = DockLayout.Calculate(height, panel, 106, 1);
        Check(top.EdgeTop == 12 && top.PanelTop == 12);
        Check(Math.Abs(bottom.EdgeTop + 106 + 12 - height) < .001);
        Check(Math.Abs(bottom.PanelTop + panel + 12 - height) < .001);
        for (int i = 0; i <= 100; i++)
        {
            double ratio = i / 100d; var placement = DockLayout.Calculate(height, panel, 106, ratio);
            Check(placement.PanelTop >= 12 && placement.PanelTop + panel <= height - 12 + .001);
            Check(placement.PanelTop >= previous); previous = placement.PanelTop;
            Check(Math.Abs(DockLayout.PositionFromEdgeTop(height, 106, placement.EdgeTop) - ratio) < .001);
        }
    }
    Check(DockLayout.Calculate(900, 702, 106, .2).PanelTop == 12);
    Check(DockLayout.Calculate(900, 702, 106, .5).PanelTop > 12);
    Check(DockLayout.Normalize(double.NaN) == .5 && DockLayout.Normalize(2) == 1 && DockLayout.Normalize(-1) == 0);
}
static void DockIntent()
{
    Check(!DockLayout.ShouldSnap(0, 200, 1200, 1200));
    Check(!DockLayout.ShouldSnap(20, 200, 1220, 1200), "vertical drag jitter docked the panel");
    Check(!DockLayout.ShouldSnap(55, 0, 1255, 1200));
    Check(!DockLayout.ShouldSnap(80, 100, 1280, 1200), "mostly vertical drag docked the panel");
    Check(!DockLayout.ShouldSnap(80, 0, 1210, 1200), "merely touching the edge docked the panel");
    Check(!DockLayout.ShouldSnap(80, 0, 1100, 1200));
    Check(DockLayout.ShouldSnap(56, 8, 1220, 1200));
    Check(DockLayout.ShouldSnap(100, -50, 1300, 1200));
    Check(!DockLayout.ShouldSnap(-80, 0, 1300, 1200));
}
static void WindowHeight()
{
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("调整大小后保留");
        service.SaveWindowHeight(460, .6, .25);
        service.SaveWindowPlacement(false, .6, .3, .25);
        var preferences = store.Snapshot().Preferences; preferences.Theme = "Dark"; service.SavePreferences(preferences);
        Check(store.State.Preferences.Height == 460 && store.State.Tasks.Single().Title == "调整大小后保留");
        Throws<ArgumentOutOfRangeException>(() => service.SaveWindowHeight(double.NaN, 0, 0));
    }
    using var reopened = new DataStore(dir.Path);
    Check(reopened.State.Preferences.Height == 460 && reopened.State.Preferences.FloatingY == .25);
    Check(reopened.State.Preferences.Theme == "Dark" && !reopened.State.Preferences.IsDocked);
}
static void HeightBounds()
{
    foreach (double work in new[] { 200d, 600d, 1080d, 1440d })
    foreach (double? requested in new double?[] { null, -10, 100, 420, 2000, double.NaN, double.PositiveInfinity })
    {
        double height = DockLayout.PanelHeight(work, requested);
        Check(double.IsFinite(height) && height > 0 && height <= work - 24);
        foreach (double position in new[] { 0d, .2, .5, .8, 1d })
        {
            var layout = DockLayout.Calculate(work, height, 148, position);
            Check(layout.PanelTop >= 12 && layout.PanelTop + height <= work - 12 + .001);
        }
    }
    Check(DockLayout.PanelHeight(900, 460) == 460);
    Check(new Preferences().AutoHideSeconds == .5);
    Check(JsonSerializer.Deserialize<Preferences>("{}", DataStore.Json)!.AutoHideSeconds == .5);
    Check(JsonSerializer.Deserialize<Preferences>("{\"autoHideSeconds\":2}", DataStore.Json)!.AutoHideSeconds == 2);
}
static void AutoHide()
{
    Check(DockLayout.CanAutoHide(false, false, false, false, false, false));
    // Only live interactions block hiding. An empty focused input or an old draft
    // is not an interaction; a closed settings window/menu must release the gate.
    for (int active = 0; active < 6; active++)
    {
        bool[] state = new bool[6]; state[active] = true;
        Check(!DockLayout.CanAutoHide(state[0], state[1], state[2], state[3], state[4], state[5]));
        state[active] = false;
        Check(DockLayout.CanAutoHide(state[0], state[1], state[2], state[3], state[4], state[5]));
    }
}
static void WindowPlacement()
{
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("位置变化后仍保留");
        service.SavePreferences(new Preferences { Theme = "Dark", AutoHideSeconds = 2, Width = 360, MonitorName = "screen" });
        service.SaveWindowPlacement(false, .7, .2, .8);
        Check(service.State.Preferences.Theme == "Dark" && service.State.Preferences.AutoHideSeconds == 2 && service.State.Preferences.MonitorName == "screen");
    }
    using (var reopened = new DataStore(dir.Path))
    {
        var preferences = reopened.State.Preferences;
        Check(!preferences.IsDocked && preferences.DockPosition == .7 && preferences.FloatingX == .2 && preferences.FloatingY == .8);
        Check(reopened.State.Tasks.Single().Title == "位置变化后仍保留");
        new TaskService(reopened).SaveWindowPlacement(true, 1, .2, .8);
    }
    using (var reopened = new DataStore(dir.Path)) Check(reopened.State.Preferences.IsDocked && reopened.State.Preferences.DockPosition == 1);
    var path = Path.Combine(dir.Path, "state.json"); var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
    foreach (string field in new[] { "dockPosition", "isDocked", "floatingX", "floatingY" }) json["preferences"]!.AsObject().Remove(field);
    File.WriteAllText(path, json.ToJsonString());
    using var oldVersion = new DataStore(dir.Path); Check(oldVersion.State.Preferences.IsDocked && oldVersion.State.Preferences.DockPosition == .5);
}
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

static void Persist()
{
    Check(System.IO.Path.GetFileName(DataStore.DefaultDirectory) == "WinKit", "product rename changed the existing data directory");
    using var dir = new TestDirectory();
    Guid id;
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store); service.AddTask("保留这件事"); id = service.State.Tasks.Single().Id;
        service.SaveCard(null, "项目仓库", "https://github.com/li5bo5/WinKit");
    }
    using var reopened = new DataStore(dir.Path);
    Check(reopened.State.Tasks.Single().Id == id);
    Check(reopened.State.Cards.Single().Name == "项目仓库");
    Check(File.Exists(reopened.PreviousPath));
    Check(Directory.GetFiles(reopened.BackupsPath).Length == 1);
}
static void Exclusive()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path);
    Throws<IOException>(() => { using var second = new DataStore(dir.Path); });
}
static void Recover()
{
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    { var service = new TaskService(store); service.SaveCard(null, "已保存", "原文"); service.AddTask("后一次提交"); }
    File.WriteAllText(System.IO.Path.Combine(dir.Path, "state.json"), "{broken");
    using (var store = new DataStore(dir.Path))
    {
        Check(store.RecoverySource != null); Check(store.State.Cards.Single().Content == "原文");
        new TaskService(store).AddTask("恢复后继续保存");
        Check(Directory.GetFiles(dir.Path, "state.corrupt-*.json").Length == 1);
    }
    using var reopened = new DataStore(dir.Path); Check(reopened.State.Tasks.Single().Title == "恢复后继续保存");
}
static void Unrecoverable()
{
    using var dir = new TestDirectory(); var path = System.IO.Path.Combine(dir.Path, "state.json");
    File.WriteAllText(path, "broken");
    Throws<InvalidDataException>(() => { using var store = new DataStore(dir.Path); });
    Check(File.ReadAllText(path) == "broken");
}
static void FutureSchema()
{
    using var dir = new TestDirectory(); var path = System.IO.Path.Combine(dir.Path, "state.json");
    var state = new AppState { SchemaVersion = 99 }; var bytes = JsonSerializer.Serialize(state, DataStore.Json); File.WriteAllText(path, bytes);
    Throws<UnsupportedSchemaException>(() => { using var store = new DataStore(dir.Path); });
    Check(File.ReadAllText(path) == bytes);
}
static void UnknownFields()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path);
    var state = store.Snapshot(); using var token = JsonDocument.Parse("{\"unknown\":42}");
    state.Extra = new() { ["optionalFuture"] = token.RootElement.Clone() };
    state.Preferences.Extra = new() { ["futurePreference"] = token.RootElement.Clone() }; store.Save(state);
    new TaskService(store).AddTask("修改已知字段");
    Check(store.State.Extra!["optionalFuture"].GetProperty("unknown").GetInt32() == 42);
    Check(store.State.Preferences.Extra!.ContainsKey("futurePreference"));
}
static void Backup()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("备份中的任务"); var export = System.IO.Path.Combine(dir.Path, "export.json"); store.Export(export);
    service.SaveCard(null, "恢复前的卡片", "需要保留"); service.RestoreBackup(export);
    Check(store.State.Cards.Count == 0); Check(store.State.Tasks.Count == 1);
    var before = Directory.GetFiles(dir.Path, "before-restore-*.json").Single();
    Check(DataStore.ReadFile(before).Cards.Single().Content == "需要保留");
}
static void FailedSave()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var before = File.ReadAllText(store.FilePath);
    File.WriteAllText(store.BackupsPath, "block backup directory creation");
    Throws<IOException>(() => new TaskService(store).AddTask("不得假装保存成功"));
    Check(store.State.Tasks.Count == 0); Check(File.ReadAllText(store.FilePath) == before);
}
static void Import()
{
    using var dir = new TestDirectory(); using var old = new TestDirectory();
    var id = Guid.NewGuid();
    var original = JsonSerializer.Serialize(new { Id = id, Title = "旧待办", CreatedAt = "2024-01-01T10:00:00+08:00", Order = 0 });
    File.WriteAllText(System.IO.Path.Combine(old.Path, "todos.jsonl"), original);
    File.WriteAllText(System.IO.Path.Combine(old.Path, "recycle_bin.jsonl"), JsonSerializer.Serialize(new { Id = Guid.NewGuid(), Title = "旧完成记录", DeletedAt = "2023-01-01T10:00:00+08:00" }));
    File.WriteAllText(System.IO.Path.Combine(old.Path, "phrases.jsonl"), "{\"Content\":\"旧短语\"}\n");
    using var store = new DataStore(dir.Path, old.Path); var service = new TaskService(store);
    Check(service.State.Tasks.Single().Id == id); Check(service.State.History.Single().Day.Year == 2023);
    Check(service.State.Cards.Single().Content == "旧短语"); Check(service.ImportLegacy(old.Path) == 0);
    Check(File.ReadAllText(System.IO.Path.Combine(old.Path, "todos.jsonl")) == original);
}
static void BadImport()
{
    using var dir = new TestDirectory(); using var old = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    File.WriteAllText(System.IO.Path.Combine(old.Path, "todos.jsonl"), "{\"Title\":\"有效行\"}\n{broken");
    Throws<InvalidDataException>(() => service.ImportLegacy(old.Path));
    Check(service.State.Tasks.Count == 0); Check(service.State.ImportedSources.Count == 0);
}
static void Recurring()
{
    using var dir = new TestDirectory(); var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(8));
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store, () => now);
        service.SaveRoutine(new Routine { Title = "每天整理", Frequency = RepeatFrequency.Daily, StartDate = service.Today });
        service.SaveRoutine(new Routine { Title = "周报", Frequency = RepeatFrequency.Weekly, StartDate = service.Today, Weekdays = [DayOfWeek.Monday] });
        service.EnsureToday(); Check(service.Pending.Count() == 2);
        var daily = service.Pending.Single(t => t.Title == "每天整理"); service.Complete(daily.Id, true);
        now = now.AddDays(1); service.EnsureToday(); Check(service.Pending.Count() == 1); Check(service.State.History.Count == 1);
        service.DeleteTask(service.Pending.Single().Id); service.EnsureToday(); Check(!service.Pending.Any());
    }
    using var reopened = new DataStore(dir.Path); var next = new TaskService(reopened, () => now); next.EnsureToday(); Check(!next.Pending.Any());
    now = now.AddDays(6); next.EnsureToday(); Check(next.Pending.Count() == 2);
    next.AddTask("同名"); next.AddTask("同名"); Check(next.Pending.Count(t => t.Title == "同名") == 2);
}
static void Trash()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    for (int i = 0; i < 12; i++)
    { service.SaveCard(null, "卡片" + i, "https://example.com/" + i); service.DeleteCard(service.State.Cards[0].Id); }
    Check(service.State.Trash.Count == 10); Check(service.State.Trash[0].Card!.Name == "卡片11"); Check(service.State.Trash[^1].Card!.Name == "卡片2");
    service.RestoreTrash(service.State.Trash[0].Id); Check(service.State.Cards.Single().Name == "卡片11"); Check(service.State.Cards.Single().Link!.AbsoluteUri.EndsWith("/11"));
}
static void ClearTrash()
{
    using var dir = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    {
        var service = new TaskService(store);
        service.SaveRoutine(new Routine { Title = "今天不做", Frequency = RepeatFrequency.Daily, StartDate = service.Today });
        service.DeleteTask(service.Pending.Single().Id);
        service.AddTask("已完成保留历史"); service.Complete(service.Pending.Single().Id, true);
        service.AddTask("保留的待办");
        service.SaveCard(null, "保留的内容", "原文"); service.SaveCard(null, "删除的内容", "不要恢复"); service.DeleteCard(service.State.Cards[0].Id);
        Check(service.State.Trash.Count == 2);
        var expected = store.Snapshot(); expected.Trash.Clear(); expected.Revision++;
        service.ClearTrash();
        Check(JsonSerializer.Serialize(service.State) == JsonSerializer.Serialize(expected), "clear changed data outside trash");
        service.EnsureToday(); Check(service.Pending.Single().Title == "保留的待办", "deleted recurrence reappeared");
    }
    using var reopened = new DataStore(dir.Path);
    Check(reopened.State.Trash.Count == 0);
    Check(reopened.State.Cards.Single().Name == "保留的内容"); Check(reopened.State.History.Single().Title == "已完成保留历史");
    Check(reopened.State.SuppressedOccurrences.Count == 1);
}
static void History()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); var service = new TaskService(store);
    service.AddTask("完成后可回看"); var id = service.Pending.Single().Id; service.Complete(id, true); service.DeleteTask(id);
    Check(service.State.History.Count == 1); service.RestoreTrash(service.State.Trash.Single().Id); Check(service.Pending.Count() == 1);
    Check(service.State.History.Single().Title == "完成后可回看"); service.Complete(id, true); service.Complete(id, false);
    Check(service.State.History.Count == 1);
}
static void Links()
{
    Check(ContentLinks.Parse("example.com/path")!.AbsoluteUri == "https://example.com/path");
    Check(ContentLinks.Parse("javascript:alert(1)") == null); Check(ContentLinks.Parse("hello@example.com") == null);
    var card = new ContentCard { Name = "名称", Content = "https://example.com/path" };
    Check(card.DisplayName == "名称"); Check(card.Link!.AbsoluteUri == card.Content);
}

static void Incomplete()
{
    using var dir = new TestDirectory(); var path = System.IO.Path.Combine(dir.Path, "state.json");
    File.WriteAllText(path, "{}"); Throws<InvalidDataException>(() => { using var store = new DataStore(dir.Path); }); Check(File.ReadAllText(path) == "{}");
}
static void BackupRetention()
{
    using var dir = new TestDirectory(); using var store = new DataStore(dir.Path); Directory.CreateDirectory(store.BackupsPath);
    for (int i = 1; i <= 16; i++) store.Export(System.IO.Path.Combine(store.BackupsPath, $"2020-01-{i:00}.json"));
    var manual = System.IO.Path.Combine(store.BackupsPath, "user-export.json"); store.Export(manual);
    new TaskService(store).AddTask("新提交"); Check(File.Exists(manual));
    Check(Directory.GetFiles(store.BackupsPath, "*.json").Length == 15);
}

static void MissingPrimary()
{
    using var dir = new TestDirectory(); using var old = new TestDirectory();
    using (var store = new DataStore(dir.Path))
    { var service = new TaskService(store); service.AddTask("有效备份里的任务"); service.SaveCard(null, "后续提交", "内容"); }
    File.Delete(System.IO.Path.Combine(dir.Path, "state.json"));
    File.WriteAllText(System.IO.Path.Combine(old.Path, "todos.jsonl"), "broken legacy file");
    using var recovered = new DataStore(dir.Path, old.Path);
    Check(recovered.State.Tasks.Single().Title == "有效备份里的任务");
    Check(recovered.State.ImportedSources.Count == 0);
}

sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "levitation-check-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
