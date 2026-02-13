namespace ClaudeCodeLauncher.Views;

/// <summary>
/// Cross-platform smart input control that properly handles predictive keyboards
/// </summary>
public class SmartInput : View
{
    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(SmartInput), "Type here...");

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SmartInput), string.Empty);

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler<string>? TextCommitted;
    public event EventHandler<string>? SpecialKeyPressed;

    internal void OnTextCommitted(string text)
    {
        Text += text;
        TextCommitted?.Invoke(this, text);
    }

    internal void OnSpecialKeyPressed(string key)
    {
        SpecialKeyPressed?.Invoke(this, key);
    }

    public void Clear()
    {
        Text = string.Empty;
    }
}
