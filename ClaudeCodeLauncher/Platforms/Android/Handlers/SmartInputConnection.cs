#if ANDROID
using Android.Views;
using Android.Views.InputMethods;
using Java.Lang;
using AndroidView = Android.Views.View;
using AndroidPaint = Android.Graphics.Paint;
using AndroidColor = Android.Graphics.Color;
using AndroidCanvas = Android.Graphics.Canvas;
namespace ClaudeCodeLauncher.Platforms.Android.Handlers;

/// <summary>
/// Custom InputConnection that properly handles predictive keyboard input (SwiftKey, Gboard, etc.)
/// 
/// THE PROBLEM: Standard terminals like Termux receive key events via sendKeyEvent().
/// Predictive keyboards (SwiftKey) send full words via commitText() which Termux drops.
/// 
/// THE FIX: This InputConnection intercepts ALL IME methods and routes them properly.
/// </summary>
public class SmartInputConnection : BaseInputConnection
{
    private readonly Action<string> _onTextCommitted;
    private readonly Action<string>? _onComposingText;
    private readonly Action<SpecialKeyEvent>? _onSpecialKey;
    private readonly AndroidView _targetView;
    
    private string _composingText = "";

    public SmartInputConnection(
        AndroidView targetView, 
        bool fullEditor,
        Action<string> onTextCommitted,
        Action<string>? onComposingText = null,
        Action<SpecialKeyEvent>? onSpecialKey = null) 
        : base(targetView, fullEditor)
    {
        _targetView = targetView;
        _onTextCommitted = onTextCommitted;
        _onComposingText = onComposingText;
        _onSpecialKey = onSpecialKey;
    }

    /// <summary>
    /// Called when predictive keyboard commits a word (SwiftKey sends this, not sendKeyEvent!)
    /// This is THE KEY METHOD that fixes SwiftKey compatibility.
    /// </summary>
    public override bool CommitText(ICharSequence? text, int newCursorPosition)
    {
        var textStr = text?.ToString() ?? "";
        
        // Clear any composing state
        _composingText = "";
        
        // Send the committed text to our handler
        _onTextCommitted?.Invoke(textStr);
        
        return true;
    }

    /// <summary>
    /// Called during composition (typing before word is finalized)
    /// Shows the in-progress word before user commits it
    /// </summary>
    public override bool SetComposingText(ICharSequence? text, int newCursorPosition)
    {
        _composingText = text?.ToString() ?? "";
        
        // Optionally show composing text preview
        _onComposingText?.Invoke(_composingText);
        
        return true;
    }

    /// <summary>
    /// Called when user finishes composing (e.g., selects prediction)
    /// </summary>
    public override bool FinishComposingText()
    {
        if (!string.IsNullOrEmpty(_composingText))
        {
            _onTextCommitted?.Invoke(_composingText);
            _composingText = "";
        }
        return true;
    }

    /// <summary>
    /// Called for physical keyboard and some special keys
    /// </summary>
    public override bool SendKeyEvent(KeyEvent? e)
    {
        if (e == null) return false;
        
        // Only handle key down events
        if (e.Action != KeyEventActions.Down) 
            return base.SendKeyEvent(e);

        var keyCode = e.KeyCode;
        
        // Handle special keys
        var specialKey = keyCode switch
        {
            Keycode.Enter => SpecialKeyEvent.Enter,
            Keycode.Tab => SpecialKeyEvent.Tab,
            Keycode.Del => SpecialKeyEvent.Backspace,
            Keycode.DpadUp => SpecialKeyEvent.ArrowUp,
            Keycode.DpadDown => SpecialKeyEvent.ArrowDown,
            Keycode.DpadLeft => SpecialKeyEvent.ArrowLeft,
            Keycode.DpadRight => SpecialKeyEvent.ArrowRight,
            Keycode.Escape => SpecialKeyEvent.Escape,
            _ => SpecialKeyEvent.None
        };

        // Handle Ctrl+key combinations
        if (e.IsCtrlPressed)
        {
            specialKey = keyCode switch
            {
                Keycode.C => SpecialKeyEvent.CtrlC,
                Keycode.D => SpecialKeyEvent.CtrlD,
                Keycode.Z => SpecialKeyEvent.CtrlZ,
                Keycode.L => SpecialKeyEvent.CtrlL,
                _ => SpecialKeyEvent.None
            };
        }

        if (specialKey != SpecialKeyEvent.None)
        {
            _onSpecialKey?.Invoke(specialKey);
            return true;
        }

        // For regular character keys, convert to text
        var unicodeChar = e.UnicodeChar;
        if (unicodeChar != 0)
        {
            _onTextCommitted?.Invoke(((char)unicodeChar).ToString());
            return true;
        }

        return base.SendKeyEvent(e);
    }

    /// <summary>
    /// Called when user deletes text (backspace, delete key)
    /// </summary>
    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        // Send backspace for each character to delete
        for (int i = 0; i < beforeLength; i++)
        {
            _onSpecialKey?.Invoke(SpecialKeyEvent.Backspace);
        }
        
        return true;
    }

	/// <summary>
	/// Tell the IME what kind of input we accept
	/// </summary>
	public override global::Android.Text.IEditable? Editable => null;
}

/// <summary>
/// Special key events for terminal control
/// </summary>
public enum SpecialKeyEvent
{
    None,
    Enter,
    Tab,
    Backspace,
    Delete,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Escape,
    CtrlC,
    CtrlD,
    CtrlZ,
    CtrlL
}
#endif
