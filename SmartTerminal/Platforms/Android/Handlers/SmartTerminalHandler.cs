#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Webkit;
using Android.Widget;
using Microsoft.Maui.Handlers;
using SmartTerminal.Views;
using System.Text;
using WebView = Android.Webkit.WebView;

namespace SmartTerminal.Platforms.Android.Handlers;

/// <summary>
/// Android handler for SmartTerminalView.
///
/// Architecture:
///   FrameLayout
///     ├── WebView (xterm.js rendering — fills entire view)
///     └── SmartInputEditText (invisible overlay — catches SwiftKey commitText)
///
/// The WebView renders the terminal. The invisible EditText steals focus and
/// captures all keyboard input including SwiftKey predictions. Input is forwarded
/// to xterm.js via EvaluateJavascript, which then sends it through the normal
/// xterm.js → C# → PTY pipeline.
/// </summary>
public class SmartTerminalHandler : ViewHandler<SmartTerminalView, FrameLayout>
{
    private WebView? _webView;
    private SmartInputEditText? _inputOverlay;
    private SmartTerminalView? _termView;

	public static IPropertyMapper<SmartTerminalView, SmartTerminalHandler> Mapper =
		new PropertyMapper<SmartTerminalView, SmartTerminalHandler>(ViewMapper);

	public SmartTerminalHandler() : base(Mapper) { }
	
    protected override FrameLayout CreatePlatformView()
    {
        var context = Context!;
        var frame = new FrameLayout(context);

        // 1. WebView — full-size, renders xterm.js
        _webView = new WebView(context);
        _webView.LayoutParameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);

        var settings = _webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = true;
        settings.AllowContentAccess = true;
        settings.MediaPlaybackRequiresUserGesture = false;

        _webView.SetWebViewClient(new TerminalWebViewClient(this));
        _webView.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#1a1a2e"));

        frame.AddView(_webView);

        // 2. SmartInputEditText — invisible, captures keyboard input
        _inputOverlay = new SmartInputEditText(context, OnSmartInput);
        _inputOverlay.LayoutParameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);
        // Make it invisible but still focusable
        _inputOverlay.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        _inputOverlay.SetTextColor(global::Android.Graphics.Color.Transparent);
        _inputOverlay.SetCursorVisible(false);
        _inputOverlay.Alpha = 0f;

        frame.AddView(_inputOverlay);

        return frame;
    }

    protected override void ConnectHandler(FrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        _termView = VirtualView;

        // Wire up C# → WebView output methods
        _termView.WriteOutput = WriteToTerminal;
        _termView.ResizeTerminal = ResizeTerminal;
        _termView.FitTerminal = FitTerminal;
        _termView.FocusTerminal = FocusTerminal;

        // Load xterm.js page
        _webView?.LoadUrl("file:///android_asset/terminal.html");
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        _termView = null;
        _webView?.Destroy();
        _webView = null;
        _inputOverlay = null;
        base.DisconnectHandler(platformView);
    }

    // --- C# → WebView ---

    private void WriteToTerminal(string data)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _webView?.EvaluateJavascript($"termWrite('{base64}')", null);
        });
    }

    private void ResizeTerminal(int cols, int rows)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _webView?.EvaluateJavascript($"termResize({cols},{rows})", null);
        });
    }

    private void FitTerminal()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _webView?.EvaluateJavascript("termFit()", null);
        });
    }

    private void FocusTerminal()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _inputOverlay?.RequestFocus();
            var imm = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
            imm?.ShowSoftInput(_inputOverlay, ShowFlags.Forced);
        });
    }

    // --- SmartInputEditText → xterm.js ---

    /// <summary>
    /// Called when the SmartInputEditText captures input from SwiftKey or any keyboard.
    /// Forwards it into xterm.js which handles the terminal emulation,
    /// then xterm.js sends it back to C# via the URL scheme for PTY writing.
    /// </summary>
    private void OnSmartInput(string text)
    {
        _termView?.RaiseInputReceived(text);
    }

    // --- WebView → C# (URL scheme interception) ---

    /// <summary>
    /// Intercepts smartterm:// URLs from xterm.js to route events to C#.
    /// </summary>
    internal void HandleTerminalMessage(string type, string base64Data)
    {
        try
        {
            var data = Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));

            switch (type)
            {
                case "input":
                    // xterm.js key event (for keys typed directly into xterm, not via our overlay)
                    _termView?.RaiseInputReceived(data);
                    break;

                case "resize":
                    var parts = data.Split(',');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int cols) &&
                        int.TryParse(parts[1], out int rows))
                    {
                        _termView?.RaiseSizeChanged(cols, rows);
                    }
                    break;

                case "ready":
                    var readyParts = data.Split(',');
                    if (readyParts.Length == 2 &&
                        int.TryParse(readyParts[0], out int readyCols) &&
                        int.TryParse(readyParts[1], out int readyRows))
                    {
                        _termView?.RaiseTerminalReady(readyCols, readyRows);
                        // Auto-focus to show keyboard
                        FocusTerminal();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandleTerminalMessage error: {ex.Message}");
        }
    }

    // --- WebViewClient ---

    private class TerminalWebViewClient : WebViewClient
    {
        private readonly SmartTerminalHandler _handler;

        public TerminalWebViewClient(SmartTerminalHandler handler) => _handler = handler;

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString();
            if (url != null && url.StartsWith("smartterm://"))
            {
                // Parse: smartterm://type?data=base64encoded
                var uri = new Uri(url);
                var type = uri.Host;
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var data = query["data"] ?? "";

                _handler.HandleTerminalMessage(type, data);
                return true; // consumed
            }

            return base.ShouldOverrideUrlLoading(view, request);
        }
    }
}

// ==========================================================
// SmartInputEditText — The SwiftKey fix.
//
// This invisible EditText overrides onCreateInputConnection to
// return our custom InputConnection that catches commitText().
// ==========================================================

internal class SmartInputEditText : EditText
{
    private readonly Action<string> _onInput;

    public SmartInputEditText(Context context, Action<string> onInput)
        : base(context)
    {
        _onInput = onInput;

        // Make it capture keyboard but not display anything
        ImeOptions = ImeAction.None;
        SetRawInputType(InputTypes.ClassText | InputTypes.TextFlagNoSuggestions);
        Focusable = true;
        FocusableInTouchMode = true;
    }

    public override IInputConnection OnCreateInputConnection(EditorInfo outAttrs)
    {
        outAttrs.InputType = InputTypes.ClassText | InputTypes.TextFlagAutoCorrect;
		outAttrs.ImeOptions = (ImeFlags)((int)ImeAction.None | 0x2000000);
        return new SmartInputConnection(this, true, _onInput);
    }
}

// ==========================================================
// SmartInputConnection — The actual commitText() interceptor.
//
// Standard terminals only handle sendKeyEvent() — they miss
// SwiftKey/Gboard predictions which arrive via commitText().
// We catch both and forward everything.
// ==========================================================

internal class SmartInputConnection : BaseInputConnection
{
    private readonly Action<string> _onInput;

    public SmartInputConnection(global::Android.Views.View targetView, bool fullEditor, Action<string> onInput)
        : base(targetView, fullEditor)
    {
        _onInput = onInput;
    }

    /// <summary>
    /// SwiftKey, Gboard, and other predictive keyboards send completed words here.
    /// Standard terminals DROP this. We don't.
    /// </summary>
    public override bool CommitText(Java.Lang.ICharSequence? text, int newCursorPosition)
    {
        var str = text?.ToString();
        if (!string.IsNullOrEmpty(str))
        {
            _onInput(str);
        }
        return true;
    }

    /// <summary>
    /// Physical keyboards and some apps send individual key events here.
    /// </summary>
    public override bool SendKeyEvent(KeyEvent? e)
    {
        if (e?.Action == KeyEventActions.Down)
        {
            // Handle special keys
            switch (e.KeyCode)
            {
                case Keycode.Enter:
                    _onInput("\r");
                    return true;

                case Keycode.Del:
                    _onInput("\x7f"); // DEL
                    return true;

                case Keycode.Tab:
                    _onInput("\t");
                    return true;

                case Keycode.Escape:
                    _onInput("\x1b");
                    return true;

                // Arrow keys → ANSI escape sequences
                case Keycode.DpadUp:
                    _onInput("\x1b[A");
                    return true;

                case Keycode.DpadDown:
                    _onInput("\x1b[B");
                    return true;

                case Keycode.DpadRight:
                    _onInput("\x1b[C");
                    return true;

                case Keycode.DpadLeft:
                    _onInput("\x1b[D");
                    return true;

                default:
                    // Regular character from physical keyboard
                    var ch = (char)e.UnicodeChar;
                    if (ch != 0)
                    {
                        // Handle Ctrl+key combinations
                        if (e.IsCtrlPressed && ch >= 'a' && ch <= 'z')
                        {
                            _onInput(((char)(ch - 'a' + 1)).ToString());
                            return true;
                        }
                        _onInput(ch.ToString());
                        return true;
                    }
                    break;
            }
        }

        return base.SendKeyEvent(e);
    }

    /// <summary>
    /// Composing text (mid-prediction) — we let it accumulate.
    /// Only commitText matters for final input.
    /// </summary>
    public override bool SetComposingText(Java.Lang.ICharSequence? text, int newCursorPosition)
    {
        // Don't send composing text to terminal — wait for commitText
        return true;
    }

    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        // Backspace from soft keyboard
        if (beforeLength > 0)
        {
            for (int i = 0; i < beforeLength; i++)
                _onInput("\x7f");
        }
        return true;
    }
}
#endif
