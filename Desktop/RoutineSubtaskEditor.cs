using System.Windows.Automation;

namespace Levitation.Desktop;

internal sealed class RoutineSubtaskEditor : StackPanel
{
    private readonly Window _owner;
    private readonly Branch _root;
    private readonly TextBlock _note = Ui.Text("每次生成时，子待办都从未完成开始。", 11, true);
    private Branch? _openComposer;

    public RoutineSubtaskEditor(Window owner, Func<string> title)
    {
        _owner = owner;
        Margin = new Thickness(0, 12, 0, 0);
        _root = new Branch(this, "", 0, null, title);
        Children.Add(_root.View);
        _note.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(_note);
        UpdateNote();
    }

    public void Load(IEnumerable<RoutineSubtask> templates)
    {
        _openComposer?.CloseComposer();
        _root.Clear();
        foreach (var template in templates) _root.Add(template);
        UpdateNote();
    }

    public List<RoutineSubtask> Collect()
    {
        _openComposer?.CommitDraft(focus: false);
        return _root.Nodes.Select(node => node.ToTemplate()).ToList();
    }

    private void UpdateNote() => _note.Visibility = _root.Nodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    private sealed class Branch
    {
        private readonly RoutineSubtaskEditor _editor;
        private readonly Branch? _parent;
        private readonly int _depth;
        private readonly Func<string> _title;
        private readonly DockPanel _row = new();
        private readonly StackPanel _rows = new(), _body = new();
        private readonly Button _add;
        private readonly Button? _caption;
        private readonly Border _branch;
        private SubtaskPanel? _composer;
        private bool _expanded;
        private string _draft = "";

        public List<Branch> Nodes { get; } = [];
        public StackPanel View { get; } = new();
        private string Title { get; set; }

        public Branch(RoutineSubtaskEditor editor, string title, int depth, Branch? parent, Func<string>? rootTitle = null)
        {
            _editor = editor;
            _depth = depth;
            _parent = parent;
            Title = title;
            _title = rootTitle ?? (() => Title);
            _expanded = depth == 0;
            _add = depth == 0
                ? SettingsLayout.Action("＋ 添加子待办", ToggleComposer)
                : TaskControls.Button(TaskControls.Glyph(TaskControls.Plus), "添加子待办", Title, ToggleComposer);
            _add.Visibility = depth < TaskTree.MaximumDepth ? Visibility.Visible : Visibility.Collapsed;
            _row.MinHeight = 30;
            _row.Background = Brushes.Transparent;
            if (depth != 0)
            {
                var remove = TaskControls.Button(TaskControls.Glyph(TaskControls.Trash), "移除子待办", Title, Remove);
                DockPanel.SetDock(remove, Dock.Right);
                _row.Children.Add(remove);
            }
            DockPanel.SetDock(_add, Dock.Right);
            _row.Children.Add(_add);

            if (depth == 0)
            {
                var heading = Ui.Text("子待办", 12);
                heading.FontWeight = FontWeights.SemiBold;
                AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
                _row.Children.Add(heading);
            }
            else
            {
                _caption = Ui.Action(Title, Toggle);
                _caption.FontSize = 12;
                _caption.MinHeight = 28;
                _caption.Margin = new Thickness(0);
                _caption.Padding = new Thickness(6, 2, 4, 2);
                _caption.Background = Brushes.Transparent;
                _caption.HorizontalContentAlignment = HorizontalAlignment.Left;
                _caption.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.F2) { Rename(); e.Handled = true; }
                };
                _caption.MouseDoubleClick += (_, e) => { Rename(); e.Handled = true; };
                var menu = Ui.CreateMenu();
                var edit = new MenuItem { Header = "编辑子待办" };
                edit.Click += (_, _) => Rename();
                menu.Items.Add(edit);
                _caption.ContextMenu = menu;
                _row.Children.Add(_caption);
            }

            _body.Children.Add(_rows);
            _branch = new Border { Child = _body, Margin = new Thickness(7, 2, 0, 3),
                BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(7, 0, 0, 0) };
            _branch.SetResourceReference(Border.BorderBrushProperty, "GuideLine");
            View.Children.Add(_row);
            View.Children.Add(_branch);
            Update();
        }

        public void Add(RoutineSubtask template)
        {
            var node = new Branch(_editor, template.Title, _depth + 1, this);
            foreach (var child in template.Children) node.Add(child);
            Nodes.Add(node);
            _rows.Children.Add(node.View);
            Update();
        }

        public void Clear()
        {
            Nodes.Clear();
            _rows.Children.Clear();
            _draft = "";
            Update();
        }

        public RoutineSubtask ToTemplate() => new(Title, Nodes.Select(node => node.ToTemplate()).ToList());

        private void Toggle()
        {
            if (Nodes.Count == 0 && _composer == null) { Rename(); return; }
            _expanded = !_expanded;
            if (!_expanded) CloseWithinBranch();
            Update();
        }

        private void ToggleComposer()
        {
            if (_composer != null) { CloseComposer(); return; }
            _editor._openComposer?.CloseComposer();
            _expanded = true;
            _composer = new SubtaskPanel(_editor._owner, _title(), _draft, text => _draft = text,
                () => CommitDraft(focus: true), panel => { if (ReferenceEquals(panel, _composer)) CloseComposer(); });
            _editor._openComposer = this;
            _body.Children.Add(_composer);
            Update();
            _composer.Input.Focus();
            _composer.Input.BringIntoView();
        }

        public void CommitDraft(bool focus)
        {
            if (string.IsNullOrWhiteSpace(_draft) || _composer == null) return;
            Add(new RoutineSubtask(_draft.Trim(), []));
            _composer.Input.Clear();
            _editor.UpdateNote();
            if (focus) _composer.Input.Focus();
        }

        public void CloseComposer()
        {
            if (_composer == null) return;
            var composer = _composer;
            _composer = null;
            _editor._openComposer = null;
            _body.Children.Remove(composer);
            if (Nodes.Count == 0 && _depth > 0) _expanded = false;
            Update();
        }

        private void CloseWithinBranch()
        {
            for (var branch = _editor._openComposer; branch != null; branch = branch._parent)
                if (branch == this) { _editor._openComposer!.CloseComposer(); return; }
        }

        private void Remove()
        {
            if (_parent == null) return;
            CloseWithinBranch();
            _parent.Nodes.Remove(this);
            _parent._rows.Children.Remove(View);
            _parent.Update();
            _editor.UpdateNote();
            _parent._add.Focus();
        }

        private void Rename()
        {
            if (_caption == null || _caption.Visibility != Visibility.Visible) return;
            var input = Ui.Input("编辑子待办名称", Title);
            input.Height = input.MinHeight = 28;
            _caption.Visibility = Visibility.Collapsed;
            _row.Children.Add(input);
            void Finish(bool commit)
            {
                if (!_row.Children.Contains(input)) return;
                if (commit && !string.IsNullOrWhiteSpace(input.Text)) Title = input.Text.Trim();
                _caption.Visibility = Visibility.Visible;
                _row.Children.Remove(input);
                Update();
            }
            input.LostKeyboardFocus += (_, _) => Finish(true);
            input.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Escape)) return;
                e.Handled = true;
                Finish(e.Key == Key.Enter);
                _caption.Focus();
            };
            input.Focus();
            input.SelectAll();
        }

        private void Update()
        {
            bool hasBody = Nodes.Count > 0 || _composer != null;
            _branch.Visibility = hasBody && _expanded ? Visibility.Visible : Visibility.Collapsed;
            if (_depth != 0)
                AutomationProperties.SetName(_add, (_composer != null ? "收起子待办输入框 " : "添加子待办 ") + Title);
            if (_caption == null) return;
            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            if (hasBody)
            {
                var arrow = TaskControls.Stroke("M 2,1 L 5,4 L 2,7", "Muted", 1.2);
                arrow.Width = 7; arrow.Height = 8; arrow.Margin = new Thickness(0, 0, 5, 0);
                arrow.RenderTransformOrigin = new Point(.5, .5);
                arrow.RenderTransform = new RotateTransform(_expanded ? 90 : 0);
                caption.Children.Add(arrow);
            }
            var label = Ui.Text(Title, 12);
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            // The title remains the only stretchable part of the row.
            var header = new DockPanel();
            if (caption.Children.Count > 0) header.Children.Add(caption);
            header.Children.Add(label);
            _caption.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _caption.Content = header;
            _caption.ToolTip = Title;
            AutomationProperties.SetName(_caption, hasBody ? (_expanded ? "折叠 " : "展开 ") + Title : "编辑 " + Title);
        }
    }
}
