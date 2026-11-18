namespace Levitation.Desktop;

internal sealed class SubtaskPanel : StackPanel
{
    public TextBox? Input { get; }

    public SubtaskPanel(Window owner, TaskItem parent, string draft,
        Action<string> draftChanged, Action save, Action closeComposer)
    {
        Input = Ui.Input("为“" + parent.Title + "”添加子待办", draft); Input.Height = Input.MinHeight = 28;
        Input.FontSize = 11.5; Input.Padding = new Thickness(8, 2, 8, 2); Input.BorderThickness = new Thickness(0); Input.Background = Brushes.Transparent;
        Input.Template = Ui.InputTemplate(decorate: false);
        var field = new Grid(); field.Children.Add(Input);
        var placeholder = Ui.Text("添加子待办...", 11.5, true); placeholder.Margin = new Thickness(8, 0, 0, 0); placeholder.Opacity = .75; placeholder.IsHitTestVisible = false;
        placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; field.Children.Add(placeholder);
        bool composing = false;
        Input.TextChanged += (_, _) => { draftChanged(Input.Text); placeholder.Visibility = Input.Text.Length == 0 && !composing ? Visibility.Visible : Visibility.Collapsed; };
        TextCompositionManager.AddPreviewTextInputStartHandler(Input, (_, _) => { composing = true; placeholder.Visibility = Visibility.Collapsed; });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Input, (_, _) => composing = true);
        Input.PreviewTextInput += (_, _) => { composing = false; placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !composing && Keyboard.Modifiers == ModifierKeys.None) { App.Try(save); e.Handled = true; }
            else if (e.Key == Key.Escape && !composing) { closeComposer(); e.Handled = true; }
        };
        var line = new DockPanel(); var enter = TaskControls.Button(TaskControls.Glyph(TaskControls.Enter, 13), "保存子待办", parent.Title, save); enter.Height = 26;
        DockPanel.SetDock(enter, Dock.Right); line.Children.Add(enter); line.Children.Add(field);
        var surface = new Border { Child = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Margin = new Thickness(4, 4, 0, 2) };
        surface.SetResourceReference(Border.BorderBrushProperty, "Line"); surface.SetResourceReference(Border.BackgroundProperty, "Paper"); Children.Add(surface);
    }
}
