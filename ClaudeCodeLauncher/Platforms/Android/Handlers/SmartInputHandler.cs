#if ANDROID
using ClaudeCodeLauncher.Platforms.Android.Handlers;
using ClaudeCodeLauncher.Views;
using Microsoft.Maui.Handlers;

namespace ClaudeCodeLauncher.Platforms.Android.Handlers;

/// <summary>
/// MAUI Handler that maps SmartInput to SmartInputView on Android
/// </summary>
public class SmartInputHandler : ViewHandler<SmartInput, SmartInputView>
{
    public static readonly IPropertyMapper<SmartInput, SmartInputHandler> PropertyMapper =
        new PropertyMapper<SmartInput, SmartInputHandler>(ViewMapper)
        {
            [nameof(SmartInput.Text)] = MapText,
            [nameof(SmartInput.Placeholder)] = MapPlaceholder
        };

    public static readonly CommandMapper<SmartInput, SmartInputHandler> CommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(SmartInput.Clear)] = MapClear
        };

    public SmartInputHandler() : base(PropertyMapper, CommandMapper)
    {
    }

    protected override SmartInputView CreatePlatformView()
    {
        var view = new SmartInputView(Context);
        
        // Wire up events
        view.TextCommitted += OnTextCommitted;
        view.ComposingTextChanged += OnComposingTextChanged;
        view.SpecialKeyPressed += OnSpecialKeyPressed;
        
        return view;
    }

    protected override void DisconnectHandler(SmartInputView platformView)
    {
        platformView.TextCommitted -= OnTextCommitted;
        platformView.ComposingTextChanged -= OnComposingTextChanged;
        platformView.SpecialKeyPressed -= OnSpecialKeyPressed;
        base.DisconnectHandler(platformView);
    }

    private void OnTextCommitted(object? sender, string text)
    {
        VirtualView?.OnTextCommitted(text);
    }

    private void OnComposingTextChanged(object? sender, string text)
    {
        // Could expose composing text preview if needed
        System.Diagnostics.Debug.WriteLine($"Composing: {text}");
    }

    private void OnSpecialKeyPressed(object? sender, SpecialKeyEvent key)
    {
        VirtualView?.OnSpecialKeyPressed(key.ToString());
    }

    private static void MapText(SmartInputHandler handler, SmartInput smartInput)
    {
        // Sync MAUI Text property to platform view
        // Only update if different to avoid loops
        if (handler.PlatformView != null)
        {
            var currentText = handler.PlatformView.GetText();
            if (currentText != smartInput.Text)
            {
                handler.PlatformView.SetText(smartInput.Text);
            }
        }
    }

    private static void MapPlaceholder(SmartInputHandler handler, SmartInput smartInput)
    {
        // Set placeholder on platform view
        handler.PlatformView?.SetPlaceholder(smartInput.Placeholder);
    }

    private static void MapClear(SmartInputHandler handler, SmartInput smartInput, object? args)
    {
        handler.PlatformView?.ClearText();
        smartInput.Text = string.Empty;
    }
}
#endif
