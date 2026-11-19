using System.ComponentModel;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace Levitation.Desktop;

internal sealed class DockWindow : Window
{
    private readonly TaskService _service;
    private readonly DataStore _store;
    private readonly Window _edge;
    private readonly Border _surface;
    private readonly DockAnimation _animation;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly StackPanel _pending = new(), _completed = new(), _cards = new();
    private readonly TextBox _taskInput = Ui.Input("添加待办"), _cardInput = Ui.Input("添加常用内容");
    private readonly DockScrollViewer _pendingScroll, _completedScroll, _body;
    private readonly TextBlock _pendingCount = Ui.Text(""), _completedCount = Ui.Text("", 11, true),
        _date = Ui.Text("", 13);
    private readonly Button _pin;
    private readonly HashSet<ContextMenu> _menus = new();
    private readonly ToggleButton _tasksToggle, _cardsToggle;
    private readonly Border _taskSurface, _cardSurface;
    private bool _reordering;
    private TaskReorderDrag? _taskSort;
    private readonly HashSet<Guid> _expandedTasks = new(), _subtaskComposers = new();
    private readonly Dictionary<Guid, string> _subtaskDrafts = new();
    private readonly Dictionary<Guid, TextBox> _subtaskInputs = new();
    private readonly Dictionary<Guid, Action<bool>> _taskExpanders = new();
    private int _refreshVersion;
    private sealed record CachedTask(string Signature, TaskTreeRow View, Action<bool> Update);
    private readonly Dictionary<Guid, CachedTask> _taskCache = new();
    private readonly Dictionary<Guid, (string Signature, Border Row)> _cardCache = new();
    private readonly TaskRowMotion _rowMotion;
    private readonly FileCardActions _fileActions;
    private readonly CardDropController _cardDrop;
    private readonly Dictionary<Guid, TaskTreeRow> _taskViews = new();
    private bool HasOpenChild => OwnedWindows.Cast<Window>().Any(window => window.IsVisible && window.WindowState != WindowState.Minimized);
    private SettingsWindow? _settings;
    private RoutineWindow? _routines;
    private DateTimeOffset? _leftAt;
    private DateOnly _shownDay;
    private bool _pinned, _stopped, _automaticCollapse;
    private DateTimeOffset _lastTyping = DateTimeOffset.MinValue;
    private Thumb? _resizeGrip;
    private bool _resizing => _resizeGrip != null;
    private Rect _resizeStartBounds, _resizeBounds;
    private int _resizeStartX, _resizeStartY;
    private double _resizeScale;
    private System.Windows.Forms.Screen? _resizeScreen;
    private UIElement? _dragHandle;
    private int _dragStartX, _dragStartY;
    private double _dragStartLeft, _dragStartTop, _dragScale, _dockPosition, _floatingX, _floatingY;
    private bool _dragMoved, _dragFromEdge, _dragWasExpanded, _docked, _snapPreview;

    public DockWindow(TaskService service, DataStore store)
    {
        Ui.ThemeWindow(this);
        _service = service; _store = store; _fileActions = new FileCardActions(this, service, store); _shownDay = service.Today;
        NativeWindow.RememberContentWindow();
        _dockPosition = DockLayout.Normalize(service.State.Preferences.DockPosition);
        _docked = service.State.Preferences.IsDocked;
        Title = "Levitation"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;

        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Margin = new Thickness(18, 12, 14, 10) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(Ui.Icon(Ui.Settings, "设置", OpenSettings));
        _pin = Ui.Icon(Ui.Pin, "保持展开", () => { _pinned = !_pinned; _pin!.Opacity = _pinned ? 1 : .55; _pin.ToolTip = _pinned ? "取消保持展开" : "保持展开"; _leftAt = null; });
        _pin.Opacity = .55; controls.Children.Add(_pin); controls.Children.Add(Ui.Icon(Ui.Arrow, "收起到右侧", () => Collapse()));
        DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
        header.Children.Add(_date);
        header.Cursor = Cursors.SizeAll; header.ToolTip = "拖动顶部移动窗口，向右靠近屏幕边缘后缩成停靠预览"; AttachDrag(header, false);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }; headerBorder.SetResourceReference(Border.BorderBrushProperty, "Line"); root.Children.Add(headerBorder);

        var body = new StackPanel { Margin = new Thickness(12, 12, 12, 10) };
        var routine = Ui.Action("日常任务  ›", OpenRoutines); routine.FontSize = 11; routine.Padding = new Thickness(9, 4, 9, 4);
        routine.ToolTip = "添加指定日期、每天或每周的任务";
        routine.MinHeight = 26; routine.Height = 26; routine.Margin = new Thickness(6, 0, 0, 0);
        _pendingScroll = new DockScrollViewer { Content = _pending };
        _completedScroll = new DockScrollViewer { Content = _completed };
        _completedScroll.SizeChanged += (_, _) => UpdateListHeights();
        var group = new Grid(); group.RowDefinitions.Add(new RowDefinition()); group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _pendingScroll.MinHeight = 96; group.Children.Add(_pendingScroll);
        Ui.RoundInput(_taskInput);
        _taskInput.ToolTip = "添加一件待办，Enter 保存";
        _taskInput.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_taskInput.Text)) { App.Try(() => { _service.AddTask(_taskInput.Text); _taskInput.Clear(); }); e.Handled = true; } };
        var addTask = Composer(_taskInput, "+", () => { if (string.IsNullOrWhiteSpace(_taskInput.Text)) return; _service.AddTask(_taskInput.Text); _taskInput.Clear(); });
        addTask.Margin = new Thickness(0, 4, 0, 5); Grid.SetRow(addTask, 1); group.Children.Add(addTask);
        var completedHeader = new Grid { Margin = new Thickness(4, 0, 0, 0) }; completedHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); completedHeader.ColumnDefinitions.Add(new ColumnDefinition());
        var completedArrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"), Width = 8, Height = 4, Stretch = Stretch.Uniform, StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        completedArrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        var arrowBox = new Border { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = completedArrow };
        completedArrow.RenderTransformOrigin = new Point(.5, .5); completedHeader.Children.Add(arrowBox);
        Grid.SetColumn(_completedCount, 1); _completedCount.VerticalAlignment = VerticalAlignment.Center; completedHeader.Children.Add(_completedCount);
        var completedToggle = new ToggleButton { Content = completedHeader, IsChecked = true, Height = 24, Margin = new Thickness(0, 5, 0, 4), Cursor = Cursors.Hand, Template = Ui.FlatToggleTemplate() };
        System.Windows.Automation.AutomationProperties.SetName(completedToggle, "今日已完成，展开或折叠");
        completedToggle.Checked += (_, _) => { _completedScroll.Visibility = Visibility.Visible; completedArrow.RenderTransform = new RotateTransform(0); };
        completedToggle.Unchecked += (_, _) => { _completedScroll.Visibility = Visibility.Collapsed; completedArrow.RenderTransform = new RotateTransform(-90); };
        var completedGroup = Ui.Stack(completedToggle, _completedScroll); Grid.SetRow(completedGroup, 2); group.Children.Add(completedGroup);
        var taskSection = Ui.Section(_pendingCount, group, routine, "待办", service.State.Preferences.TasksExpanded,
            () => DockLayout.SectionHeight(true, _service.State.Preferences.TasksHeight), DockLayout.TaskSectionMinimum,
            open => _service.SaveSectionExpanded(true, open), height => _service.SaveSectionHeight(true, height));
        _tasksToggle = taskSection.Toggle; _taskSurface = taskSection.Surface; body.Children.Add(_taskSurface);
        _taskSurface.SizeChanged += (_, _) => UpdateListHeights();

        var cardGroup = new Grid(); cardGroup.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cardGroup.RowDefinitions.Add(new RowDefinition());
        var cardHeading = Ui.Text("常用内容", 12); cardHeading.FontWeight = FontWeights.SemiBold;
        _cardInput.ToolTip = "Ctrl+V 粘贴截图或文字，也可拖入文字、网址和文件";
        _cardInput.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_cardInput.Text)) { App.Try(SaveNewCard); e.Handled = true; } };
        Ui.RoundInput(_cardInput);
        cardGroup.Children.Add(Composer(_cardInput, "保存", SaveNewCard));
        var cardsScroll = new DockScrollViewer { Content = _cards, MinHeight = 72, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(cardsScroll, 1); cardGroup.Children.Add(cardsScroll);
        var cardSection = Ui.Section(cardHeading, cardGroup, null, "常用内容", service.State.Preferences.CardsExpanded,
            () => DockLayout.SectionHeight(false, _service.State.Preferences.CardsHeight), DockLayout.CardSectionMinimum,
            open => _service.SaveSectionExpanded(false, open), height => _service.SaveSectionHeight(false, height));
        _cardsToggle = cardSection.Toggle; _cardSurface = cardSection.Surface; _cardSurface.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(_cardSurface);
        ClipboardImagePaste.Attach(_cardSurface, _cardInput, image =>
        {
            if (_stopped) return;
            _service.AddPastedImage(image);
            cardsScroll.ScrollToTop();
        });
        _body = new DockOverlayScrollViewer { Content = body, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(_body, 1); root.Children.Add(_body);
        _rowMotion = new TaskRowMotion(_body);
        root.Children.Add(CreateResizeGrip(horizontal: false, vertical: true));
        root.Children.Add(CreateResizeGrip(horizontal: true, vertical: false));
        root.Children.Add(CreateResizeGrip(horizontal: true, vertical: true));
        _surface = Ui.Card(root, new Thickness(0)); _surface.CornerRadius = new CornerRadius(19); _surface.SetResourceReference(Border.BackgroundProperty, "Paper"); Content = new AdornerDecorator { Child = _surface };
        _edge = new Window { Title = "Levitation 侧边入口", Width = 20, Height = 148, Opacity = .72, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, Topmost = true, ShowActivated = false, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize };
        _animation = new DockAnimation(this, _edge, _surface);
        Ui.ThemeWindow(_edge);
        var edgeMark = new Border { Width = 3, Height = 48, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = .55 };
        edgeMark.SetResourceReference(Border.BackgroundProperty, "Muted");
        var edgeHandle = new Border { Child = edgeMark, CornerRadius = new CornerRadius(10, 0, 0, 10), BorderThickness = new Thickness(1, 1, 0, 1), ToolTip = "悬停展开，按住上下拖动" };
        edgeHandle.SetResourceReference(Border.BackgroundProperty, "Paper"); edgeHandle.SetResourceReference(Border.BorderBrushProperty, "Line");
        System.Windows.Automation.AutomationProperties.SetName(edgeHandle, "展开待办浮窗，拖动调整位置");
        edgeHandle.MouseEnter += (_, _) =>
        {
            if (_dragHandle != null || Mouse.LeftButton == MouseButtonState.Pressed) return;
            if (_animation.IsRunning && !_animation.Expanding) Expand();
            else if (!_animation.IsRunning && !IsVisible) _hover.Start();
        };
        edgeHandle.MouseLeave += (_, _) => _hover.Stop(); AttachDrag(edgeHandle, true); _edge.Content = edgeHandle;
        _hover.Tick += (_, _) => { _hover.Stop(); if (_dragHandle == null && Mouse.LeftButton != MouseButtonState.Pressed && NativeWindow.PointerInside(_edge)) Expand(); };
        PreviewKeyDown += CancelDrag; _edge.PreviewKeyDown += CancelDrag;
        Deactivated += (_, _) => CancelTaskReorder();
        PreviewTextInput += (_, _) => { _lastTyping = DateTimeOffset.UtcNow; _leftAt = null; };
        PreviewKeyDown += (_, _) => { if (Keyboard.FocusedElement is TextBox) { _lastTyping = DateTimeOffset.UtcNow; _leftAt = null; } };
        _edge.Closing += (_, e) => { if (!App.Current.Exiting) { e.Cancel = true; } };
        Closing += (_, e) => { if (!App.Current.Exiting) { e.Cancel = true; Collapse(); } };
        _cardDrop = new CardDropController(this, _edge, _cardSurface, Expand,
            files => { _service.AddFileCards(files); cardsScroll.ScrollToTop(); },
            text => { _service.SaveCard(null, "", text, expandSection: true); cardsScroll.ScrollToTop(); });
        _service.Changed += Refresh;
        _poll.Tick += Poll;
        _body.SizeChanged += (_, _) => UpdateListHeights();
        Refresh();
    }

    private static DockPanel Composer(TextBox input, string label, Action submit)
    {
        var panel = new DockPanel(); var button = Ui.Action(label, submit, true); button.FontSize = 11; button.Padding = new Thickness(9, 3, 9, 3);
        button.Height = button.MinHeight = 30; button.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(button, Dock.Right); panel.Children.Add(button); panel.Children.Add(input); return panel;
    }
    public void StartDocked() { Reposition(); if (_docked) _edge.Show(); else Show(); Reposition(); _poll.Start(); }
    public void Stop() { _stopped = true; _resizeGrip?.CancelDrag(); CancelTaskReorder(); _cardDrop.Dispose(); _rowMotion.Stop(); _poll.Stop(); _hover.Stop(); _animation.Cancel(false); EndDrag(false); _service.Changed -= Refresh; }
    public void Reposition()
    {
        if (_animation.IsRunning || _resizing) return;
        var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
        double available = NativeWindow.WorkHeight(screen);
        Width = DockLayout.PanelWidth(screen.WorkingArea.Width / NativeWindow.Scale(screen), _service.State.Preferences.Width);
        Height = DockLayout.PanelHeight(available, _service.State.Preferences.Height);
        if (_dragHandle == null)
        {
            var preferences = _service.State.Preferences; _dockPosition = DockLayout.Normalize(preferences.DockPosition); _docked = preferences.IsDocked;
            _floatingX = DockLayout.Normalize(preferences.FloatingX); _floatingY = DockLayout.Normalize(preferences.FloatingY);
        }
        if (IsVisible)
        {
            if (_docked) NativeWindow.Place(this, screen, false, _dockPosition, _edge.Height);
            else NativeWindow.PlaceFloating(this, screen, _floatingX, _floatingY);
        }
        if (_edge.IsVisible) NativeWindow.Place(_edge, screen, true, _dockPosition, _edge.Height);
        UpdateListHeights();
    }
    public void Expand()
    {
        if (_stopped || _dragHandle != null || _resizing) return;
        _leftAt = null; _hover.Stop();
        if (_animation.IsRunning) { AnimateDock(true); return; }
        if (IsVisible) { _edge.Hide(); Reposition(); return; }
        _surface.Opacity = 0; Show(); Reposition(); AnimateDock(true);
    }
    private void Collapse(bool automatic = false)
    {
        if (HasOpenChild) return;
        if (_dragHandle != null || _resizing || !IsVisible) return;
        if (_animation.IsRunning && !_animation.Expanding) return;
        _automaticCollapse = automatic;
        if (!_docked && !_animation.IsRunning)
        {
            var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
            var position = NativeWindow.RelativePosition(this, screen);
            _dockPosition = DockLayout.PositionFromEdgeTop(NativeWindow.WorkHeight(screen), _edge.Height, position.Top + Height / 2 - _edge.Height / 2);
        }
        AnimateDock(false);
    }
    private void AnimateDock(bool expand)
    {
        if (_animation.IsRunning && _animation.Expanding == expand) return;
        if (_animation.IsRunning)
        {
            _animation.Reverse(expand, CompleteDockTransition);
            return;
        }
        var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
        double scale = NativeWindow.Scale(screen);
        double edgeTop = DockLayout.Calculate(screen.WorkingArea.Height / scale, Height, _edge.Height, _dockPosition).EdgeTop;
        var entry = new Rect(screen.WorkingArea.Width / scale - _edge.Width, edgeTop, _edge.Width, _edge.Height);
        _animation.Play(expand, entry, screen, _service.State.Preferences.DockTransition, CompleteDockTransition);
    }

    private void CompleteDockTransition(bool expanded)
    {
        if (!expanded && !_docked) App.Try(() => _service.SaveWindowPlacement(true, _dockPosition, _floatingX, _floatingY));
        _leftAt = null; Reposition();
    }
    private void SetEdgeOpacity(double opacity)
    {
        _edge.BeginAnimation(OpacityProperty, null); _edge.Opacity = opacity;
    }
    private void Poll(object? sender, EventArgs args)
    {
        NativeWindow.RememberContentWindow();
        if (_shownDay != _service.Today) { _shownDay = _service.Today; App.Try(() => _service.EnsureToday()); Refresh(); }
        if (_animation.IsRunning)
        {
            if (!_animation.Expanding && ((_automaticCollapse && _animation.PointerKeepsPanelOpen(_docked)) || NativeWindow.PointerInside(_edge))) Expand();
            return;
        }
        if (!IsVisible) { _leftAt = null; return; }
        _menus.RemoveWhere(menu => !menu.IsOpen);
        bool typing = IsActive && Keyboard.FocusedElement is TextBox && (DateTimeOffset.UtcNow - _lastTyping).TotalSeconds < .8;
        var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
        if (!DockLayout.CanAutoHide(NativeWindow.PointerKeepsPanelOpen(this, screen, _docked) || NativeWindow.PointerInside(_edge), _pinned,
            _dragHandle != null || _resizing || _reordering || IsMouseCaptureWithin || _cardDrop.IsActive, HasOpenChild, _menus.Count > 0 || Ui.HasOpenMenu, typing))
        { _leftAt = null; return; }
        _leftAt ??= DateTimeOffset.UtcNow;
        if ((DateTimeOffset.UtcNow - _leftAt.Value).TotalSeconds >= Math.Clamp(_service.State.Preferences.AutoHideSeconds, .2, 30)) Collapse(true);
    }
    private void AttachDrag(UIElement handle, bool fromEdge)
    {
        handle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_dragHandle != null || _resizing || _animation.IsRunning) return;
            if (!fromEdge)
                for (var node = e.OriginalSource as DependencyObject; node != null && node != handle; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node is System.Windows.Controls.Primitives.ButtonBase or TextBox or ComboBox) return;
            if (!NativeWindow.TryCursor(out _dragStartX, out _dragStartY)) return;
            var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
            _dragScale = NativeWindow.Scale(screen);
            var position = NativeWindow.RelativePosition(fromEdge ? _edge : this, screen);
            _dragStartLeft = position.Left; _dragStartTop = position.Top;
            _dragHandle = handle; _dragFromEdge = fromEdge; _dragWasExpanded = IsVisible; _snapPreview = fromEdge; _dragMoved = false; _leftAt = null; _hover.Stop();
            if (!handle.CaptureMouse()) { _dragHandle = null; return; }
            e.Handled = true;
        };
        handle.PreviewMouseMove += (_, e) =>
        {
            if (_dragHandle != handle || e.LeftButton != MouseButtonState.Pressed || !NativeWindow.TryCursor(out int x, out int y)) return;
            double dx = (x - _dragStartX) / _dragScale, dy = (y - _dragStartY) / _dragScale;
            if (!_dragMoved && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragMoved = true;
            var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
            double workHeight = NativeWindow.WorkHeight(screen), workWidth = screen.WorkingArea.Width / _dragScale;
            if (_snapPreview) _dockPosition = DockLayout.PositionFromEdgeTop(workHeight, _edge.Height, _dragStartTop + dy);
            else
            {
                _docked = false;
                _floatingX = DockLayout.PositionFromEdgeTop(workWidth, Width, _dragStartLeft + dx);
                _floatingY = DockLayout.PositionFromEdgeTop(workHeight, Height, _dragStartTop + dy);
                // Require a deliberate push to the right; vertical motion alone never docks.
                if (DockLayout.ShouldSnap(dx, dy, _dragStartLeft + dx + Width, workWidth) && !HasOpenChild)
                {
                    _dockPosition = DockLayout.PositionFromEdgeTop(workHeight, _edge.Height, (y - screen.WorkingArea.Top) / _dragScale - _edge.Height / 2);
                    _edge.Show(); NativeWindow.Place(_edge, screen, true, _dockPosition, _edge.Height);
                    // Keep the original window and handle alive until mouse-up. Hiding
                    // it or transferring capture between HWNDs interrupts this gesture.
                    _snapPreview = true; _docked = true; SetEdgeOpacity(.82); _dragStartX = x; _dragStartY = y;
                    _dragStartTop = DockLayout.Calculate(workHeight, Height, _edge.Height, _dockPosition).EdgeTop;
                    _surface.Opacity = 0;
                }
            }
            Reposition(); e.Handled = true;
        };
        handle.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragHandle != handle) return;
            bool click = _dragFromEdge && !_dragMoved; EndDrag(true); if (click) Expand(); e.Handled = true;
        };
        handle.LostMouseCapture += (_, e) => { if (_dragHandle == handle && !handle.IsMouseCaptured) EndDrag(true); };
    }
    private void CancelDrag(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_taskSort != null) { CancelTaskReorder(); e.Handled = true; }
        else if (_resizeGrip != null) { _resizeGrip.CancelDrag(); e.Handled = true; }
        else if (_dragHandle != null) { EndDrag(false); e.Handled = true; }
    }
    private void EndDrag(bool save)
    {
        var handle = _dragHandle; if (handle == null) return;
        double position = _dockPosition, x = _floatingX, y = _floatingY; bool moved = _dragMoved, docked = _docked, expanded = _dragWasExpanded;
        _dragHandle = null; _dragMoved = false; _snapPreview = false; handle.ReleaseMouseCapture(); _leftAt = null; SetEdgeOpacity(.72);
        if (save && moved) App.Try(() => _service.SaveWindowPlacement(docked, position, x, y));
        if (_stopped) return;
        bool showPanel = save && moved ? !_service.State.Preferences.IsDocked : expanded;
        if (showPanel) { _edge.Hide(); _surface.Opacity = 1; Show(); } else { Hide(); _surface.Opacity = 1; _edge.Show(); }
        Reposition();
    }
    private Thumb CreateResizeGrip(bool horizontal, bool vertical)
    {
        bool corner = horizontal && vertical;
        string label = corner ? "调整窗口宽度和高度" : horizontal ? "调整窗口宽度" : "调整窗口高度";
        var thumb = new Thumb
        {
            Width = horizontal ? corner ? 21 : 8 : double.NaN,
            Height = vertical ? corner ? 21 : 8 : double.NaN,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
            Margin = corner ? new Thickness(0) : horizontal ? new Thickness(0, 17, 0, 21) : new Thickness(21, 0, 21, 0),
            Cursor = corner ? Cursors.SizeNESW : horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            Focusable = true,
            IsTabStop = false,
            FocusVisualStyle = null,
            ToolTip = label
        };
        System.Windows.Automation.AutomationProperties.SetName(thumb, label);
        var gripArea = new FrameworkElementFactory(typeof(Border));
        gripArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var mark = new FrameworkElementFactory(corner ? typeof(System.Windows.Shapes.Path) : typeof(Border));
        mark.SetValue(UIElement.OpacityProperty, .55);
        if (corner)
        {
            // Concentric with the window's 19 DIP corner, in the 21 DIP grip area.
            mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 4.57,9.29 A 15.5,15.5 0 0 0 11.71,16.43"));
            mark.SetValue(System.Windows.Shapes.Shape.StretchProperty, Stretch.None);
            mark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ControlLine");
            mark.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.3d);
            mark.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        }
        else
        {
            mark.SetValue(FrameworkElement.WidthProperty, horizontal ? 2d : 28d);
            mark.SetValue(FrameworkElement.HeightProperty, horizontal ? 28d : 2d);
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, horizontal ? VerticalAlignment.Center : VerticalAlignment.Bottom);
            mark.SetValue(FrameworkElement.MarginProperty, horizontal ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 0, 2));
            mark.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
            mark.SetResourceReference(Border.BackgroundProperty, "ControlLine");
        }
        mark.SetValue(UIElement.IsHitTestVisibleProperty, false);
        gripArea.AppendChild(mark);
        thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripArea };
        Grid.SetRowSpan(thumb, 2);
        thumb.DragStarted += (_, _) => BeginResize(thumb);
        thumb.DragDelta += (_, _) => ResizeFromPointer(thumb, horizontal, vertical);
        thumb.DragCompleted += (_, e) => EndResize(thumb, e.Canceled, horizontal, vertical);
        return thumb;
    }

    private void BeginResize(Thumb thumb)
    {
        if (_dragHandle != null || _resizing || _reordering || _animation.IsRunning ||
            !NativeWindow.TryCursor(out _resizeStartX, out _resizeStartY)) { thumb.CancelDrag(); return; }
        var screen = NativeWindow.Screen(_service.State.Preferences.MonitorName);
        _resizeScreen = screen;
        _resizeScale = NativeWindow.Scale(screen);
        var (left, top) = NativeWindow.RelativePosition(this, screen);
        _resizeStartBounds = _resizeBounds = new Rect(left, top, Width, Height);
        _resizeGrip = thumb;
        _leftAt = null;
        thumb.Focus();
    }

    private void ResizeFromPointer(Thumb thumb, bool horizontal, bool vertical)
    {
        if (_resizeGrip != thumb || _resizeScreen is not { } screen || !NativeWindow.TryCursor(out int x, out int y)) return;
        double? dx = horizontal ? (x - _resizeStartX) / _resizeScale : null;
        double? dy = vertical ? (y - _resizeStartY) / _resizeScale : null;
        var start = _resizeStartBounds;
        var (left, top, width, height) = DockLayout.ResizePanel(start.Left, start.Top, start.Width, start.Height,
            screen.WorkingArea.Width / _resizeScale, screen.WorkingArea.Height / _resizeScale, dx, dy);
        var updated = new Rect(left, top, width, height);
        if (updated == _resizeBounds) return;
        _resizeBounds = updated;
        NativeWindow.PlaceBounds(this, screen, updated);
        UpdateListHeights();
    }

    private void EndResize(Thumb thumb, bool cancelled, bool horizontal, bool vertical)
    {
        if (_resizeGrip != thumb || _resizeScreen is not { } screen) return;
        if (!cancelled) ResizeFromPointer(thumb, horizontal, vertical);
        _resizeGrip = null;
        _resizeScreen = null;
        _leftAt = null;
        if (_stopped) return;
        var bounds = _resizeBounds;
        bool widthChanged = Math.Abs(bounds.Width - _resizeStartBounds.Width) >= 1;
        bool heightChanged = Math.Abs(bounds.Height - _resizeStartBounds.Height) >= 1;
        if (!cancelled && (widthChanged || heightChanged))
        {
            double workWidth = screen.WorkingArea.Width / _resizeScale, workHeight = screen.WorkingArea.Height / _resizeScale;
            double position = DockLayout.PositionFromEdgeTop(workHeight, _edge.Height, bounds.Top + (bounds.Height - _edge.Height) / 2);
            double floatingX = _docked ? _floatingX : DockLayout.PositionFromEdgeTop(workWidth, bounds.Width, bounds.Left);
            double floatingY = DockLayout.PositionFromEdgeTop(workHeight, bounds.Height, bounds.Top);
            App.Try(() => _service.SaveWindowSize(widthChanged ? bounds.Width : null, heightChanged ? bounds.Height : null,
                position, floatingX, floatingY));
        }
        Reposition();
    }
    private void UpdateListHeights()
    {
        if (_body == null) return;
        double height = _taskSurface?.ActualHeight > 0 ? _taskSurface.ActualHeight : _service.State.Preferences.TasksHeight;
        bool expanded = _service.CompletedToday.Any(t => t.Subtasks.Count > 0 && _expandedTasks.Contains(t.Id));
        _completedScroll.MaxHeight = Math.Min(expanded ? 150 : 80, Math.Max(0, height - DockLayout.TaskSectionMinimum));
    }
    private void Refresh()
    {
        if (_reordering || _stopped) return;
        var pending = _service.Pending.ToList(); var completed = _service.CompletedToday.ToList();
        double scroll = _pendingScroll.VerticalOffset, doneScroll = _completedScroll.VerticalOffset;
        int refreshVersion = ++_refreshVersion;
        var nodes = TaskTree.Walk(pending.Concat(completed)).ToList();
        _expandedTasks.IntersectWith(nodes.Select(n => n.Item.Id));
        _subtaskComposers.IntersectWith(nodes.Where(n => !n.Root.CompletedAt.HasValue && !TaskTree.IsCompleted(n.Item) && n.Depth < TaskTree.MaximumDepth).Select(n => n.Item.Id));
        foreach (var node in nodes) if (node.Item.Subtasks.Count == 0 && !_subtaskComposers.Contains(node.Item.Id)) _expandedTasks.Remove(node.Item.Id);
        var allIds = TaskTree.Walk(_service.State.Tasks).Select(n => n.Item.Id).ToHashSet();
        foreach (var id in _subtaskDrafts.Keys.Where(id => !allIds.Contains(id)).ToArray()) _subtaskDrafts.Remove(id);
        _taskExpanders.Clear(); _subtaskInputs.Clear(); _taskViews.Clear();
        _date.Text = DateTime.Now.ToString("M月d日 dddd"); _pendingCount.Text = $"待办  {pending.Count}";
        _pendingCount.FontWeight = FontWeights.SemiBold;
        _tasksToggle.IsChecked = _service.State.Preferences.TasksExpanded; _cardsToggle.IsChecked = _service.State.Preferences.CardsExpanded;
        _taskSurface.Height = _service.State.Preferences.TasksExpanded ? DockLayout.SectionHeight(true, _service.State.Preferences.TasksHeight) : double.NaN;
        _cardSurface.Height = _service.State.Preferences.CardsExpanded ? DockLayout.SectionHeight(false, _service.State.Preferences.CardsHeight) : double.NaN;
        _completedCount.Text = $"今日已完成  {completed.Count}";
        foreach (var menu in _menus.ToArray()) menu.IsOpen = false;
        _menus.Clear();
        var pendingRows = pending.Select(t => TaskRow(t)).ToList(); var completedRows = completed.Select(t => TaskRow(t)).ToList();
        if (pending.Count == 0) { var empty = Ui.Text("今天的待办都完成了。", 12, true); empty.Margin = new Thickness(3, 9, 0, 9); pendingRows.Add(empty); }
        if (completed.Count == 0) { var empty = Ui.Text("完成的任务会显示在这里。", 11, true); empty.Margin = new Thickness(3, 6, 0, 6); completedRows.Add(empty); }
        Reconcile(_pending, pendingRows); Reconcile(_completed, completedRows);
        foreach (var id in _taskCache.Keys.Where(id => !nodes.Any(n => n.Item.Id == id)).ToArray()) _taskCache.Remove(id);
        var cards = new List<FrameworkElement>();
        foreach (var card in _service.State.Cards)
        {
            string signature = System.Text.Json.JsonSerializer.Serialize(card, DataStore.Json);
            if (!_cardCache.TryGetValue(card.Id, out var cached) || cached.Signature != signature)
                _cardCache[card.Id] = cached = (signature, CardRow(card));
            cards.Add(cached.Row);
        }
        Reconcile(_cards, cards);
        foreach (var id in _cardCache.Keys.Where(id => !_service.State.Cards.Any(c => c.Id == id)).ToArray()) _cardCache.Remove(id);
        UpdateListHeights();
        _settings?.RefreshData(); _routines?.RefreshList();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_stopped || refreshVersion != _refreshVersion) return;
            _pendingScroll.ScrollToVerticalOffset(scroll); _completedScroll.ScrollToVerticalOffset(doneScroll);
        }));
    }
    private static void Reconcile(StackPanel panel, IEnumerable<FrameworkElement> desired)
    {
        var items = desired.ToArray();
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (index < panel.Children.Count && panel.Children[index] == item) continue;
            if (item.Parent is Panel previous) previous.Children.Remove(item);
            panel.Children.Insert(index, item);
        }
        while (panel.Children.Count > items.Length) panel.Children.RemoveAt(panel.Children.Count - 1);
    }
    private FrameworkElement TaskRow(TaskItem task, Guid? parentId = null, int depth = 0, TaskItem? root = null)
    {
        root ??= task;
        string signature = System.Text.Json.JsonSerializer.Serialize(new { task.Title, task.Description, task.Activity, task.CompletedAt,
            Done = TaskTree.IsCompleted(task), parentId, depth, RootCompleted = root.CompletedAt.HasValue });
        if (_taskCache.TryGetValue(task.Id, out var cached) && cached.Signature == signature)
        {
            _taskViews[task.Id] = cached.View; _taskExpanders[task.Id] = cached.Update;
            Reconcile(cached.View.Children, task.Subtasks.Select(child => TaskRow(child, task.Id, depth + 1, root)));
            cached.Update(false); return cached.View.Group;
        }
        bool isRoot = parentId == null, completed = TaskTree.IsCompleted(task);
        bool running = isRoot && !completed && task.Activity == TaskActivity.Running;
        bool paused = isRoot && !completed && task.Activity == TaskActivity.Paused;
        bool canAdd = !root.CompletedAt.HasValue && !completed && depth < TaskTree.MaximumDepth;
        var row = new Grid { Tag = task.Id, Background = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1), MinHeight = 30 };
        row.ToolTip = Ui.TaskToolTip(task.Title, task.Description);
        ToolTipService.SetShowDuration(row, 20000);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var highlight = new Border { CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, IsHitTestVisible = false };
        if (running) highlight.SetResourceReference(Border.BackgroundProperty, "Soft"); Grid.SetColumnSpan(highlight, 4); row.Children.Add(highlight);
        string status = completed ? "已完成" : running ? "正在做" : paused ? "已暂停" : "未开始";
        System.Windows.Automation.AutomationProperties.SetItemStatus(row, status);
        double markSize = isRoot ? 16 : 14;
        UIElement mark = completed ? TaskControls.Glyph(TaskControls.Check, markSize) : running || paused ? new TaskProgressMark(running) : TaskControls.EmptyBox();
        if (!isRoot && mark is Border box) box.Width = box.Height = 14;
        var complete = TaskControls.Button(mark, completed ? "撤销完成" : "完成待办", task.Title,
            () => { if (parentId is Guid parent) _service.CompleteSubtask(parent, task.Id, !completed); else CompleteParent(task.Id, !completed); }, left: true, emphasis: true);
        complete.Height = 28; complete.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetItemStatus(complete, status); Grid.SetColumn(complete, 1); row.Children.Add(complete);
        var text = Ui.Text(task.Title, isRoot ? 12 : 11.5, completed); text.Margin = new Thickness(0, 4, 3, 4);
        text.FontFamily = Ui.TaskFont; text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis;
        string textColor = completed || paused ? "Muted" : running ? "Ink" : "TaskInk";
        text.SetResourceReference(TextBlock.ForegroundProperty, textColor); text.FontWeight = running ? FontWeights.SemiBold : FontWeights.Normal;
        if (completed) text.TextDecorations = TextDecorations.Strikethrough;
        if (task.RoutineId.HasValue)
        {
            var repeatMark = new Run("↻ "); repeatMark.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            if (text.Inlines.FirstInline is { } first) text.Inlines.InsertBefore(first, repeatMark); else text.Inlines.Add(repeatMark);
        }
        var title = new Grid(); title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); title.Children.Add(text);
        var count = Ui.Text($"{task.Subtasks.Count(c => c.IsCompleted)}/{task.Subtasks.Count}", 10, true); count.Margin = new Thickness(3, 0, 0, 0); Grid.SetColumn(count, 1); title.Children.Add(count);
        var arrow = TaskControls.Stroke("M 2,1 L 5,4 L 2,7", "Muted", 1.2); arrow.Width = 7; arrow.Height = 8; arrow.RenderTransformOrigin = new Point(.5, .5);
        var arrowBox = new Border { Child = arrow, Margin = new Thickness(5, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(arrowBox, 2); title.Children.Add(arrowBox); Grid.SetColumn(title, 2); row.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isRoot && !completed)
        {
            var completeSlot = new Grid { Width = 24 };
            if (running || paused) completeSlot.Children.Add(TaskControls.Button(TaskControls.Glyph(TaskControls.Check), "完成待办", task.Title, () => CompleteParent(task.Id, true), emphasis: true)); actions.Children.Add(completeSlot);
            var play = TaskControls.Button(TaskControls.Glyph(running ? TaskControls.Pause : TaskControls.Play), running ? "暂停待办" : paused ? "继续待办" : "开始待办", task.Title,
                () => { if (running) _service.PauseTask(task.Id); else _service.StartTask(task.Id); }); play.Margin = new Thickness(1, 0, 0, 0); actions.Children.Add(play);
        }
        Button? add = null;
        if (canAdd)
        {
            add = TaskControls.Button(TaskControls.Glyph(TaskControls.Plus), "添加子待办", task.Title, () => ToggleSubtaskComposer(task.Id)); add.Margin = new Thickness(actions.Children.Count > 0 ? 1 : 0, 0, 0, 0); actions.Children.Add(add);
        }
        var delete = TaskControls.Button(TaskControls.Glyph(TaskControls.Trash), "删除待办", task.Title,
            () => { if (parentId is Guid parent) _service.DeleteSubtask(parent, task.Id); else _service.DeleteTask(task.Id); });
        delete.Margin = new Thickness(actions.Children.Count > 0 ? 1 : 0, 0, 0, 0); delete.Opacity = .28; actions.Children.Add(delete); Grid.SetColumn(actions, 3); row.Children.Add(actions);
        row.MouseEnter += (_, _) => { highlight.SetResourceReference(Border.BackgroundProperty, "TaskHoverSurface"); text.SetResourceReference(TextBlock.ForegroundProperty, "TaskHoverInk"); text.FontWeight = FontWeights.Bold; delete.Opacity = 1; };
        row.MouseLeave += (_, _) => { if (running) highlight.SetResourceReference(Border.BackgroundProperty, "Soft"); else highlight.Background = Brushes.Transparent; text.SetResourceReference(TextBlock.ForegroundProperty, textColor); text.FontWeight = running ? FontWeights.SemiBold : FontWeights.Normal; delete.Opacity = .28; };
        var menu = Ui.CreateMenu(); var edit = new MenuItem { Header = isRoot ? "编辑待办" : "编辑子待办" }; edit.Click += (_, _) => EditTask(task); menu.Items.Add(edit);
        if (!isRoot || !completed)
            foreach (var (label, direction) in new[] { ("上移", -1), ("下移", 1) })
            {
                var move = new MenuItem { Header = label }; move.Click += (_, _) => App.Try(() =>
                {
                    var items = parentId is Guid parent ? TaskTree.Find(_service.State.Tasks, parent)!.Item.Subtasks.Cast<TaskItem>().ToList() : _service.Pending.ToList();
                    int index = items.FindIndex(t => t.Id == task.Id), target = index + direction;
                    if (index >= 0 && target >= 0 && target < items.Count) _service.MoveTask(new TaskMove(task.Id, items[target].Id, direction > 0 ? TaskDropPosition.After : TaskDropPosition.Before));
                }); menu.Items.Add(move);
            }
        var remove = new MenuItem { Header = "移入垃圾堆" }; remove.Click += (_, _) => App.Try(() => { if (parentId is Guid parent) _service.DeleteSubtask(parent, task.Id); else _service.DeleteTask(task.Id); }); menu.Items.Add(remove); row.ContextMenu = menu; TrackMenu(menu);
        var group = new Grid { Tag = task.Id }; group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); group.Children.Add(row);
        double indent = depth == 0 ? 12 : 8;
        var body = new Border { Margin = new Thickness(indent, 2, 0, 6), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(indent - 1, 0, 0, 2), ClipToBounds = true, Visibility = Visibility.Collapsed };
        body.SetResourceReference(Border.BorderBrushProperty, "GuideLine"); Grid.SetRow(body, 1); group.Children.Add(body);
        var children = new StackPanel(); var contents = new StackPanel(); contents.Children.Add(children); body.Child = contents;
        var view = new TaskTreeRow(task.Id, depth, group, row, body, children, root.CompletedAt.HasValue ? _completedScroll : _pendingScroll); _taskViews[task.Id] = view;
        Reconcile(children, task.Subtasks.Select(child => TaskRow(child, task.Id, depth + 1, root)));
        int animationVersion = 0; SubtaskPanel? composer = null;
        void UpdateChildren(bool animate)
        {
            var current = TaskTree.Find(_service.State.Tasks, task.Id)?.Item ?? task;
            count.Text = $"{current.Subtasks.Count(c => c.IsCompleted)}/{current.Subtasks.Count}";
            int version = ++animationVersion; bool composing = canAdd && _subtaskComposers.Contains(task.Id);
            bool open = _expandedTasks.Contains(task.Id) && (current.Subtasks.Count > 0 || composing);
            arrowBox.Visibility = current.Subtasks.Count > 0 || composing ? Visibility.Visible : Visibility.Collapsed; arrow.RenderTransform = new RotateTransform(open ? 90 : 0);
            count.Visibility = current.Subtasks.Count > 0 && row.ActualWidth >= 230 ? Visibility.Visible : Visibility.Collapsed;
            row.Cursor = current.Subtasks.Count > 0 || composing ? Cursors.Hand : Cursors.Arrow;
            if (add != null) { add.ToolTip = composing ? "收起添加框" : "添加子待办"; add.Opacity = composing ? 1 : .65; if (composing) add.SetResourceReference(Control.BackgroundProperty, "Soft"); else add.Background = Brushes.Transparent; }
            double from = body.IsVisible ? body.ActualHeight : 0; body.BeginAnimation(HeightProperty, null);
            if (!composing && composer != null) { contents.Children.Remove(composer); composer = null; _subtaskInputs.Remove(task.Id); }
            if (composing && composer == null)
            {
                composer = new SubtaskPanel(this, task, _subtaskDrafts.GetValueOrDefault(task.Id, ""), value => _subtaskDrafts[task.Id] = value, () => SaveSubtask(task.Id), () => CloseSubtaskComposer(task.Id));
                contents.Children.Add(composer); if (composer.Input != null) _subtaskInputs[task.Id] = composer.Input;
            }
            if (composer?.Input != null) _subtaskInputs[task.Id] = composer.Input;
            body.IsHitTestVisible = open;
            if (!(animate && group.IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation)) { body.Visibility = open ? Visibility.Visible : Visibility.Collapsed; body.Height = double.NaN; UpdateListHeights(); return; }
            body.Visibility = Visibility.Visible; contents.Measure(new Size(Math.Max(1, row.ActualWidth - indent * 2), double.PositiveInfinity));
            double target = open ? contents.DesiredSize.Height + 2 : 0; body.Height = from;
            var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            animation.Completed += (_, _) => { if (version != animationVersion) return; body.BeginAnimation(HeightProperty, null); body.Height = double.NaN; if (!open) body.Visibility = Visibility.Collapsed; };
            body.BeginAnimation(HeightProperty, animation); UpdateListHeights();
        }
        _taskExpanders[task.Id] = UpdateChildren;
        row.SizeChanged += (_, _) => count.Visibility = (TaskTree.Find(_service.State.Tasks, task.Id)?.Item.Subtasks.Count ?? 0) > 0 && row.ActualWidth >= 230 ? Visibility.Visible : Visibility.Collapsed;
        AttachTaskInteraction(view, task.Title, !isRoot || !completed);
        _taskCache[task.Id] = new CachedTask(signature, view, UpdateChildren); UpdateChildren(false); return group;
    }
    private void ToggleSubtasks(Guid id)
    {
        var task = TaskTree.Find(_service.State.Tasks, id)?.Item;
        if (task == null || (task.Subtasks.Count == 0 && !_subtaskComposers.Contains(id))) return;
        if (!_expandedTasks.Add(id))
        {
            _expandedTasks.Remove(id);
            foreach (var node in TaskTree.Walk([task]))
                if (_subtaskComposers.Remove(node.Item.Id) && node.Item.Id != id && _taskExpanders.TryGetValue(node.Item.Id, out var childUpdate)) childUpdate(false);
        }
        if (_taskExpanders.TryGetValue(id, out var update)) update(true);
    }
    private void ToggleSubtaskComposer(Guid id)
    {
        var location = TaskTree.Find(_service.Pending, id); if (location == null || location.Depth >= TaskTree.MaximumDepth || TaskTree.IsCompleted(location.Item)) return;
        if (_subtaskComposers.Contains(id)) { CloseSubtaskComposer(id); return; }
        _subtaskComposers.Add(id); _expandedTasks.Add(id);
        if (_taskExpanders.TryGetValue(id, out var update)) update(true);
        FocusSubtaskInput(id);
    }
    private void CloseSubtaskComposer(Guid id)
    {
        _subtaskComposers.Remove(id);
        if (TaskTree.Find(_service.State.Tasks, id)?.Item.Subtasks.Count == 0) _expandedTasks.Remove(id);
        if (_taskExpanders.TryGetValue(id, out var update)) update(true);
    }
    private void SaveSubtask(Guid id)
    {
        if (_service.AddSubtask(id, _subtaskDrafts.GetValueOrDefault(id, "")) == Guid.Empty) return;
        _subtaskDrafts.Remove(id);
        // Changed rebuilds the panel synchronously; clear and focus the current input.
        if (_subtaskInputs.TryGetValue(id, out var input)) input.Clear();
        FocusSubtaskInput(id);
    }
    private void FocusSubtaskInput(Guid id) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        if (_stopped || !_subtaskComposers.Contains(id) || !_subtaskInputs.TryGetValue(id, out var input) || !input.IsVisible) return;
        input.Focus(); Keyboard.Focus(input); input.CaretIndex = input.Text.Length; input.BringIntoView();
    }));
    private void CompleteParent(Guid id, bool completed)
    {
        var expanded = _expandedTasks.ToHashSet(); var composing = _subtaskComposers.ToHashSet();
        if (completed && TaskTree.Find(_service.State.Tasks, id) is { } task)
            foreach (var node in TaskTree.Walk([task.Item])) { _expandedTasks.Remove(node.Item.Id); _subtaskComposers.Remove(node.Item.Id); }
        try { _service.Complete(id, completed); }
        catch
        {
            _expandedTasks.Clear(); _expandedTasks.UnionWith(expanded); _subtaskComposers.Clear(); _subtaskComposers.UnionWith(composing);
            throw;
        }
    }
    private void AttachTaskInteraction(TaskTreeRow view, string title, bool reorder)
    {
        var group = view.Group; var row = view.Header; var id = view.Id;
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(row, "单击展开或折叠，双击编辑；按住文字行拖动排序或移入其他待办，最多三层子待办");
        bool pressed = false; Point origin = new(); bool? expandedBeforeClick = null;
        row.Focusable = true;
        row.PreviewKeyDown += (_, e) => { if (Keyboard.FocusedElement == row && (e.Key is Key.Space or Key.Enter)) { ToggleSubtasks(id); e.Handled = true; } };
        static DependencyObject? Parent(DependencyObject node) => node is Visual ? VisualTreeHelper.GetParent(node)
            : node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_reordering) return;
            // Let the actual checkbox and action buttons handle their own clicks.
            for (var node = e.OriginalSource as DependencyObject; node != null && node != row; node = Parent(node))
                if (node is ButtonBase or TextBox) return;
            if (e.ClickCount == 2)
            {
                pressed = false; row.ReleaseMouseCapture();
                if (expandedBeforeClick.HasValue)
                {
                    if (expandedBeforeClick.Value) _expandedTasks.Add(id); else _expandedTasks.Remove(id);
                    if (_taskExpanders.TryGetValue(id, out var update)) update(false);
                }
                if (TaskTree.Find(_service.State.Tasks, id)?.Item is { } selectedTask) EditTask(selectedTask);
                e.Handled = true; return;
            }
            expandedBeforeClick = null; row.Focus(); origin = e.GetPosition(group); pressed = row.CaptureMouse(); e.Handled = true;
        };
        row.PreviewMouseLeftButtonUp += (_, e) => { if (!pressed) return; pressed = false; row.ReleaseMouseCapture(); expandedBeforeClick = _expandedTasks.Contains(id); ToggleSubtasks(id); e.Handled = true; };
        row.LostMouseCapture += (_, _) => { if (!row.IsMouseCaptured) pressed = false; };
        row.PreviewMouseMove += (_, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(group);
            if (Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressed = false; row.ReleaseMouseCapture(); _leftAt = null;
            if (!reorder) { e.Handled = true; return; }
            App.Try(() =>
            {
                foreach (var update in _taskExpanders.Values.ToArray()) update(false);
                var expanded = _expandedTasks.ToHashSet();
                var drag = new TaskReorderDrag(_body, view, _taskViews.Values.ToArray(), [_pendingScroll, _completedScroll], origin,
                    move => _service.CanMoveTask(move), target => { _expandedTasks.Add(target.Id); if (_taskExpanders.TryGetValue(target.Id, out var expand)) expand(false); },
                    move => FinishTaskReorder(move, expanded), _rowMotion, nodeId => _taskViews.GetValueOrDefault(nodeId));
                _taskSort = drag; _reordering = true;
                try { if (!drag.Begin()) { _taskSort = null; _reordering = false; } }
                catch { drag.Cancel(); throw; }
            });
            e.Handled = true;
        };
    }
    internal void CancelTaskReorder() => _taskSort?.Cancel();
    private void FinishTaskReorder(TaskMove? move, HashSet<Guid> expandedBefore)
    {
        _taskSort = null; _reordering = false; _leftAt = null; if (_stopped) return;
        _expandedTasks.Clear(); _expandedTasks.UnionWith(expandedBefore);
        long revision = _service.State.Revision;
        try
        {
            if (move == null || !_service.CanMoveTask(move)) return;
            var target = TaskTree.Find(_service.State.Tasks, move.TargetId)!;
            var parent = move.Position == TaskDropPosition.Inside ? target : target.Parent == null ? null : TaskTree.Find(_service.State.Tasks, target.Parent.Id);
            while (parent != null) { _expandedTasks.Add(parent.Item.Id); parent = parent.Parent == null ? null : TaskTree.Find(_service.State.Tasks, parent.Parent.Id); }
            App.Try(() => _service.MoveTask(move));
        }
        finally { if (_service.State.Revision == revision) Refresh(); }
    }
    private Border CardRow(ContentCard card)
    {
        if (card.IsFileReference || card.IsPastedImage) return _fileActions.CreateRow(card);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Ui.Text("", 12); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = card.Content;
        if (card.Link is { } link)
        {
            var anchor = new Hyperlink(new Run(card.DisplayName)) { NavigateUri = link, TextDecorations = null };
            anchor.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            anchor.RequestNavigate += (_, e) => { App.Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })); e.Handled = true; };
            title.Inlines.Add(anchor);
        }
        else { title.Text = card.DisplayName; title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) EditCard(card); }; }
        content.Children.Add(title);
        grid.Children.Add(content);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Ui.Icon(Ui.Copy, "复制实际内容", () => System.Windows.Clipboard.SetText(card.Content)));
        actions.Children.Add(Ui.Icon(Ui.Delete, "删除卡片", () => _service.DeleteCard(card.Id))); Grid.SetColumn(actions, 1); grid.Children.Add(actions);
        var border = new Border { Child = grid, Background = Brushes.Transparent, Padding = new Thickness(2, 4, 0, 4), BorderThickness = new Thickness(0, 0, 0, 1), ToolTip = "右键编辑名称和内容" };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        var menu = Ui.CreateMenu(); var edit = new MenuItem { Header = "编辑名称和内容" }; edit.Click += (_, _) => EditCard(card); menu.Items.Add(edit); border.ContextMenu = menu; TrackMenu(menu);
        return border;
    }
    private void TrackMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => { _menus.Add(menu); _leftAt = null; };
        menu.Closed += (_, _) => { _menus.Remove(menu); _leftAt = null; };
    }
    private void EditTask(TaskItem task)
    {
        App.Try(() =>
        {
            var dialog = new TaskEditor(this, task.Title, task.Description, (value, description) => _service.EditTask(task.Id, value, description));
            dialog.ShowDialog();
        });
    }
    private void SaveNewCard()
    {
        if (string.IsNullOrWhiteSpace(_cardInput.Text)) return;
        _service.SaveCard(null, "", _cardInput.Text);
        _cardInput.Clear();
        _cardInput.Focus();
    }
    private void EditCard(ContentCard card)
    {
        App.Try(() =>
        {
            var dialog = new CardEditor(this, card.Name, card.Content,
                (name, content) => _service.SaveCard(card.Id, name, content));
            dialog.ShowDialog();
        });
    }
    public void OpenSettings()
    {
        var screen = NativeWindow.ContentScreen();
        if (_settings == null)
        {
            _settings = new SettingsWindow(_service, _store, Reposition);
            _settings.Closed += (_, _) => { _settings = null; Reposition(); Refresh(); };
        }
        NativeWindow.ShowCentered(_settings, screen);
    }
    public void OpenRoutines()
    {
        Expand();
        if (_routines != null) { Ui.BringForward(_routines); return; }
        _routines = new RoutineWindow(this, _service); _routines.Closed += (_, _) => _routines = null; _routines.Show();
    }
}
