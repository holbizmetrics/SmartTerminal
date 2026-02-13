#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using AndroidView = Android.Views.View;
using AndroidPaint = Android.Graphics.Paint;
using AndroidColor = Android.Graphics.Color;
using AndroidCanvas = Android.Graphics.Canvas;

namespace ClaudeCodeLauncher.Platforms.Android.Handlers;

/// <summary>
/// Custom View that properly handles predictive keyboard input.
/// Use this instead of EditText when you need full IME control.
/// </summary>
[Register("com.holger.claudecodelauncher.SmartInputView")]
public class SmartInputView : AndroidView
{
    private AndroidPaint? _textPaint;
    private AndroidPaint? _cursorPaint;
    private AndroidPaint? _placeholderPaint;
    private string _currentText = "";
    private string _composingText = "";
    private string _placeholder = "Type here...";
    private bool _cursorVisible = true;
    private global::Android.OS.Handler? _blinkHandler;
    private bool _disposed;
    
    public event EventHandler<string>? TextCommitted;
    public event EventHandler<string>? ComposingTextChanged;
    public event EventHandler<SpecialKeyEvent>? SpecialKeyPressed;
    
    public SmartInputView(Context context) : base(context)
    {
        _textPaint = CreateTextPaint();
        _cursorPaint = CreateCursorPaint();
        _placeholderPaint = CreatePlaceholderPaint();
        Initialize();
    }

    public SmartInputView(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
        _textPaint = CreateTextPaint();
        _cursorPaint = CreateCursorPaint();
        _placeholderPaint = CreatePlaceholderPaint();
        Initialize();
    }

    public SmartInputView(Context context, IAttributeSet? attrs, int defStyleAttr) 
        : base(context, attrs, defStyleAttr)
    {
        _textPaint = CreateTextPaint();
        _cursorPaint = CreateCursorPaint();
        _placeholderPaint = CreatePlaceholderPaint();
        Initialize();
    }

    private static AndroidPaint CreateTextPaint() => new AndroidPaint
    {
        AntiAlias = true,
        Color = AndroidColor.White,
        TextSize = 48f
    };

    private static AndroidPaint CreateCursorPaint() => new AndroidPaint
    {
        Color = AndroidColor.Cyan,
        StrokeWidth = 2f
    };

    private static AndroidPaint CreatePlaceholderPaint() => new AndroidPaint
    {
        AntiAlias = true,
        Color = AndroidColor.Gray,
        TextSize = 48f
    };

    private void Initialize()
    {
        Focusable = true;
        FocusableInTouchMode = true;
        
        // Start cursor blink
        StartCursorBlink();
    }

    private void StartCursorBlink()
    {
        _blinkHandler = new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!);
        ScheduleCursorBlink();
    }

    private void ScheduleCursorBlink()
    {
        _blinkHandler?.PostDelayed(() =>
        {
            if (IsAttachedToWindow && _blinkHandler != null)
            {
                _cursorVisible = !_cursorVisible;
                Invalidate();
                ScheduleCursorBlink();
            }
        }, 500);
    }

    protected override void OnDetachedFromWindow()
    {
        // Cancel cursor blink to prevent memory leak
        _blinkHandler?.RemoveCallbacksAndMessages(null);
        _blinkHandler = null;
        base.OnDetachedFromWindow();
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        // Restart cursor blink when reattached
        if (_blinkHandler == null && !_disposed)
        {
            StartCursorBlink();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Cancel handler
                _blinkHandler?.RemoveCallbacksAndMessages(null);
                _blinkHandler = null;
                
                // Dispose Paint objects
                _textPaint?.Dispose();
                _cursorPaint?.Dispose();
                _placeholderPaint?.Dispose();
                _textPaint = null;
                _cursorPaint = null;
                _placeholderPaint = null;
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs != null)
        {
            // Configure IME behavior - ALLOW SUGGESTIONS for SwiftKey/Gboard
            // This is THE KEY: we want predictive input, that's the whole point!
            outAttrs.InputType = InputTypes.ClassText | InputTypes.TextFlagAutoCorrect;
            outAttrs.ImeOptions = (ImeFlags)ImeAction.None;
            
            // Tell IME we want to receive all input
            outAttrs.InitialSelStart = 0;
            outAttrs.InitialSelEnd = 0;
        }

        return new SmartInputConnection(
            this,
            fullEditor: false,
            onTextCommitted: OnTextCommitted,
            onComposingText: OnComposingText,
            onSpecialKey: OnSpecialKey
        );
    }

    private void OnTextCommitted(string text)
    {
        _currentText += text;
        _composingText = "";
        TextCommitted?.Invoke(this, text);
        Invalidate();
    }

    private void OnComposingText(string text)
    {
        _composingText = text;
        ComposingTextChanged?.Invoke(this, text);
        Invalidate();
    }

    private void OnSpecialKey(SpecialKeyEvent key)
    {
        if (key == SpecialKeyEvent.Backspace && _currentText.Length > 0)
        {
            _currentText = _currentText[..^1];
            Invalidate();
        }
        
        SpecialKeyPressed?.Invoke(this, key);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action == MotionEventActions.Down)
        {
            // Request focus and show keyboard
            RequestFocus();
            ShowKeyboard();
            return true;
        }
        return base.OnTouchEvent(e);
    }

    public void ShowKeyboard()
    {
        var imm = Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
        imm?.ShowSoftInput(this, ShowFlags.Forced);
    }

    public void HideKeyboard()
    {
        var imm = Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
        imm?.HideSoftInputFromWindow(WindowToken, HideSoftInputFlags.None);
    }

    public void ClearText()
    {
        _currentText = "";
        _composingText = "";
        Invalidate();
    }

    public void SetText(string text)
    {
        _currentText = text ?? "";
        _composingText = "";
        Invalidate();
    }

    public void SetPlaceholder(string placeholder)
    {
        _placeholder = placeholder ?? "Type here...";
        Invalidate();
    }

    public string GetText() => _currentText;

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        
        if (canvas == null || _textPaint == null || _cursorPaint == null) return;

        // Draw background
        canvas.DrawColor(AndroidColor.ParseColor("#1e1e1e"));
        
        var x = 20f;
        var y = Height / 2f + _textPaint.TextSize / 3;
        
        // Show placeholder if empty
        if (string.IsNullOrEmpty(_currentText) && string.IsNullOrEmpty(_composingText) && _placeholderPaint != null)
        {
            canvas.DrawText(_placeholder, x, y, _placeholderPaint);
        }
        else
        {
            // Draw prompt and text
            var prompt = "> ";
            var displayText = prompt + _currentText + _composingText;
            canvas.DrawText(displayText, x, y, _textPaint);
            
            // Draw composing underline
            if (!string.IsNullOrEmpty(_composingText))
            {
                var startX = x + _textPaint.MeasureText(prompt + _currentText);
                var endX = startX + _textPaint.MeasureText(_composingText);
                canvas.DrawLine(startX, y + 5, endX, y + 5, _cursorPaint);
            }
        }
        
        // Draw cursor
        if (_cursorVisible && IsFocused)
        {
            var prompt = "> ";
            var displayText = prompt + _currentText + _composingText;
            var cursorX = x + _textPaint.MeasureText(displayText);
            canvas.DrawLine(cursorX, y - _textPaint.TextSize, cursorX, y + 10, _cursorPaint);
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var textSize = _textPaint?.TextSize ?? 48f;
        var desiredHeight = (int)(textSize * 2);
        var height = ResolveSize(desiredHeight, heightMeasureSpec);
        var width = ResolveSize(500, widthMeasureSpec);
        SetMeasuredDimension(width, height);
    }
}
#endif
