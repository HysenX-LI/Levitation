namespace Levitation.Desktop;

// Application-owned confirmations use the same theme as the other windows.
// Keep a native fallback for startup failures before resources are available.
internal static class ThemedMessageBox
{
    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowCore(null, message, caption, buttons, image, defaultResult);

    public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowCore(owner, message, caption, buttons, image, defaultResult);

    private static MessageBoxResult ShowCore(Window? owner, string message, string caption, MessageBoxButton buttons,
        MessageBoxImage image, MessageBoxResult defaultResult)
    {
        MessageBoxResult Native() => owner?.IsVisible == true
            ? System.Windows.MessageBox.Show(owner, message, caption, buttons, image, defaultResult)
            : System.Windows.MessageBox.Show(message, caption, buttons, image, defaultResult);
        if (System.Windows.Application.Current?.Resources[typeof(Window)] is not Style) return Native();
        try
        {
            owner ??= System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(window => window.IsVisible && window.IsActive);
            var dialog = new Window
            {
                Title = caption, Width = Math.Min(440, SystemParameters.WorkArea.Width - 40), SizeToContent = SizeToContent.Height,
                MaxHeight = SystemParameters.WorkArea.Height - 40, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Topmost = owner?.Topmost ?? true
            };
            if (owner?.IsVisible == true) dialog.Owner = owner;
            Ui.ThemeWindow(dialog);
            var panel = new StackPanel { Margin = new Thickness(20) };
            var heading = Ui.Text(caption, 14); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 12);
            if (image == MessageBoxImage.Error) heading.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            panel.Children.Add(heading);
            var body = Ui.Scroll(Ui.Text(message)); body.MaxHeight = Math.Clamp(SystemParameters.WorkArea.Height - 210, 100, 360); panel.Children.Add(body);
            var choices = buttons switch
            {
                MessageBoxButton.OKCancel => new[] { ("取消", MessageBoxResult.Cancel), ("确定", MessageBoxResult.OK) },
                MessageBoxButton.YesNo => new[] { ("否", MessageBoxResult.No), ("是", MessageBoxResult.Yes) },
                MessageBoxButton.YesNoCancel => new[] { ("取消", MessageBoxResult.Cancel), ("否", MessageBoxResult.No), ("是", MessageBoxResult.Yes) },
                _ => new[] { ("确定", MessageBoxResult.OK) }
            };
            var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
            var initial = defaultResult != MessageBoxResult.None ? defaultResult : buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
            var actions = new List<UIElement>();
            Button? defaultButton = null;
            foreach (var (text, value) in choices)
            {
                var button = Ui.Action(text, () => { result = value; dialog.Close(); }, value is MessageBoxResult.OK or MessageBoxResult.Yes);
                button.MinWidth = 76; button.IsDefault = value == initial;
                if (button.IsDefault) defaultButton = button;
                actions.Add(button);
            }
            dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
            dialog.Loaded += (_, _) => defaultButton?.Focus();
            panel.Children.Add(Ui.Buttons(actions.ToArray())); dialog.Content = panel; dialog.ShowDialog(); return result;
        }
        catch { return Native(); }
    }
}
