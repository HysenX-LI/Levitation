using System.Windows.Threading;

namespace Levitation.Desktop;

internal sealed class QuickCaptureWindow : Window
{
    private readonly TaskService _service;
    private readonly TextBox _input = Ui.Input("快速记录待办");
    private readonly TextBlock _placeholder = Ui.Text("计划做点什么...", 20, true);
    private bool _composing;
    private IntPtr _previous;

    public QuickCaptureWindow(TaskService service)
    {
        _service = service; Ui.ThemeWindow(this);
        Title = "Levitation · 快速记录"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Width = 560; Height = 72;
        _input.Height = _input.MinHeight = 48; _input.FontSize = 20; _input.FontFamily = Ui.TaskFont; _input.Padding = new Thickness(2, 0, 2, 0);
        _input.BorderThickness = new Thickness(0); _input.Background = Brushes.Transparent;
        _input.Template = Ui.InputTemplate(decorate: false);
        _placeholder.FontFamily = Ui.TaskFont; _placeholder.Opacity = .65; _placeholder.Margin = new Thickness(2, 0, 2, 0);
        _placeholder.TextWrapping = TextWrapping.NoWrap; _placeholder.IsHitTestVisible = false;
        var field = new Grid(); field.Children.Add(_input); field.Children.Add(_placeholder);
        var enter = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 15,1 L 15,7 Q 15,10 12,10 L 2,10 M 6,6 L 2,10 L 6,14"),
            Width = 18, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Opacity = .65, IsHitTestVisible = false
        };
        enter.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(field); Grid.SetColumn(enter, 1); row.Children.Add(enter);
        var surface = Ui.Card(row, new Thickness(22, 11, 22, 11)); surface.CornerRadius = new CornerRadius(26); surface.BorderThickness = new Thickness(0);
        surface.SetResourceReference(Border.BackgroundProperty, "Paper"); Content = surface;
        surface.MouseLeftButtonDown += (_, _) => _input.Focus();
        _input.TextChanged += (_, _) => UpdatePlaceholder();
        TextCompositionManager.AddPreviewTextInputStartHandler(_input, (_, _) => { _composing = true; UpdatePlaceholder(); });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(_input, (_, _) => { _composing = true; UpdatePlaceholder(); });
        _input.PreviewTextInput += (_, _) => { _composing = false; UpdatePlaceholder(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Dismiss(true); e.Handled = true; }
            else if (e.Key == Key.Enter && !_composing && Keyboard.Modifiers == ModifierKeys.None) { Save(); e.Handled = true; }
        };
        Deactivated += (_, _) => { if (IsVisible && !Ui.HasOpenMenu) Dismiss(false); };
        Closing += (_, e) => { if (!App.Current.Exiting) { e.Cancel = true; Dismiss(false); } };
    }
    public void Toggle()
    {
        if (IsVisible) { Dismiss(true); return; }
        _previous = NativeWindow.ForegroundHandle;
        var screen = NativeWindow.ForegroundScreen(); double scale = NativeWindow.Scale(screen);
        Width = Math.Min(560, Math.Max(240, screen.WorkingArea.Width / scale - 48)); Height = 72;
        UpdatePlaceholder();
        Opacity = 0; Show();
        NativeWindow.PlaceBounds(this, screen, new Rect((screen.WorkingArea.Width / scale - Width) / 2, (screen.WorkingArea.Height / scale - Height) / 2, Width, Height));
        Opacity = 1; NativeWindow.FocusWindow(this);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) { _input.Focus(); Keyboard.Focus(_input); _input.CaretIndex = _input.Text.Length; } }));
    }
    private void Save()
    {
        string title = _input.Text.Trim(); if (title.Length == 0) return;
        App.Try(() => { _service.AddTask(title); _input.Clear(); _composing = false; Dismiss(true); });
    }
    private void UpdatePlaceholder() => _placeholder.Visibility = _input.Text.Length == 0 && !_composing ? Visibility.Visible : Visibility.Collapsed;
    private void Dismiss(bool restoreFocus)
    {
        Hide(); _composing = false; UpdatePlaceholder();
        if (restoreFocus) NativeWindow.RestoreForeground(_previous);
    }
}
