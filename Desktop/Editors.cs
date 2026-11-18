namespace Levitation.Desktop;

internal sealed class TaskEditor : Window
{
    private readonly TextBox _text, _description;
    public string Value => _text.Text.Trim();
    public TaskEditor(Window owner, string value, string? description, Action<string, string> commit)
    {
        Ui.ThemeWindow(this);
        Title = "编辑待办"; Owner = owner; Width = 440; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 60); ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; _text = Ui.Input("待办内容", value, true);
        _description = Ui.Input("说明，可选", description ?? "", true);
        var fields = Ui.Stack(Ui.Field("待办内容", _text)); Ui.Label(fields, "说明（可选）", _description);
        panel.Children.Add(Ui.Card(fields, new Thickness(16)));
        var save = Ui.Action("保存", () => { if (Value.Length > 0) { commit(Value, _description.Text); DialogResult = true; } }, true); save.IsDefault = true;
        var cancel = Ui.Action("取消", () => DialogResult = false); cancel.IsCancel = true; panel.Children.Add(Ui.Buttons(cancel, save)); Content = Ui.Scroll(panel);
        save.MinWidth = cancel.MinWidth = 80;
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}

internal sealed class CardEditor : Window
{
    private readonly TextBox _name, _content;
    public string CardName => _name.Text.Trim();
    public string CardContent => _content.Text.Trim();
    public CardEditor(Window owner, string name, string content, Action<string, string> commit)
    {
        Ui.ThemeWindow(this);
        Title = "名称和实际内容"; Owner = owner; Width = 460; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 60); ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        _name = Ui.Input("名称，可选", name); _name.MaxLength = 80; _content = Ui.Input("内容或网址", content, true);
        var fields = new StackPanel(); fields.Children.Add(Ui.Field("名称（可选）", _name)); Ui.Label(fields, "内容或网址", _content); panel.Children.Add(Ui.Card(fields, new Thickness(16)));
        var save = Ui.Action("保存卡片", () => { if (CardContent.Length > 0) { commit(CardName, CardContent); DialogResult = true; } }, true); save.IsDefault = true;
        var cancel = Ui.Action("取消", () => DialogResult = false); cancel.IsCancel = true; panel.Children.Add(Ui.Buttons(cancel, save)); Content = Ui.Scroll(panel);
        save.MinWidth = cancel.MinWidth = 80;
        Loaded += (_, _) => _name.Focus();
    }
}
