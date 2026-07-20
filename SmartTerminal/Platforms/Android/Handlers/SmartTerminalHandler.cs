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
/// <summary>
/// FrameLayout that re-measures its children against its own laid-out size.
/// MAUI arranges a custom handler's platform view by calling Layout() directly,
/// without an Android measure pass — so MatchParent children (the WebView) never
/// get measured against the real size and collapse to 0x0. Forcing MeasureChildren
/// in OnLayout makes them fill correctly.
/// </summary>
internal sealed class TerminalFrameLayout : FrameLayout
{
    public TerminalFrameLayout(Context context) : base(context) { }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var w = right - left;
        var h = bottom - top;
        if (w > 0 && h > 0)
        {
            MeasureChildren(
                MeasureSpec.MakeMeasureSpec(w, MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(h, MeasureSpecMode.Exactly));
        }
        base.OnLayout(changed, left, top, right, bottom);
    }
}

public class SmartTerminalHandler : ViewHandler<SmartTerminalView, FrameLayout>
{
    private WebView? _webView;
    private SmartInputEditText? _inputOverlay;
    private ExtraKeysBar? _extraKeysBar;
    private SmartTerminalView? _termView;
    private bool _htmlLoaded;

	public static IPropertyMapper<SmartTerminalView, SmartTerminalHandler> Mapper =
		new PropertyMapper<SmartTerminalView, SmartTerminalHandler>(ViewMapper);

	public SmartTerminalHandler() : base(Mapper) { }

    protected override FrameLayout CreatePlatformView()
    {
        var context = Context!;
        var density = context.Resources!.DisplayMetrics!.Density;
        var frame = new TerminalFrameLayout(context);

        var extraKeysHeight = (int)(42 * density);

        // 1. WebView — fills view except extra keys bar at bottom
        _webView = new WebView(context);
        var webViewLp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);
        webViewLp.BottomMargin = extraKeysHeight;
        _webView.LayoutParameters = webViewLp;

        var settings = _webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = true;
        settings.AllowContentAccess = true;
        settings.AllowFileAccessFromFileURLs = false;
        settings.AllowUniversalAccessFromFileURLs = false;
        settings.MediaPlaybackRequiresUserGesture = false;

        _webView.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#1a1a2e"));

        // Custom clients for JS↔C# communication
        _webView.SetWebViewClient(new TerminalWebViewClient());
        _webView.SetWebChromeClient(new TerminalChromeClient(this));

        frame.AddView(_webView);

        // 2. SmartInputEditText — tiny (1x1), captures keyboard input via InputConnection.
        //    Must NOT be fullscreen — that blocks all touch events to the WebView.
        _inputOverlay = new SmartInputEditText(context, OnSmartInput);
        _inputOverlay.LayoutParameters = new FrameLayout.LayoutParams(1, 1);
        _inputOverlay.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        _inputOverlay.SetTextColor(global::Android.Graphics.Color.Transparent);
        _inputOverlay.SetCursorVisible(false);
        _inputOverlay.Alpha = 0f;

        frame.AddView(_inputOverlay);

        // Tap on terminal area → focus EditText to show/keep soft keyboard
        _webView.Touch += (sender, e) =>
        {
            if (e.Event?.Action == MotionEventActions.Down && _inputOverlay != null)
            {
                _inputOverlay.RequestFocus();
                var imm = (InputMethodManager?)context.GetSystemService(Context.InputMethodService);
                imm?.ShowSoftInput(_inputOverlay, ShowFlags.Implicit);
            }
            e.Handled = false; // let WebView handle touch too (scroll, select)
        };

        // 3. ExtraKeysBar — anchored at bottom, above soft keyboard
        _extraKeysBar = new ExtraKeysBar(context, OnExtraKeyInput, OnPasteRequested, OnCopyRequested, OnScrollRequested);
        var barLp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            extraKeysHeight,
            GravityFlags.Bottom);
        _extraKeysBar.LayoutParameters = barLp;

        frame.AddView(_extraKeysBar);

        return frame;
    }

    // A WebView-backed terminal wants ALL available space, but a bare custom MAUI
    // View is sized to its DESIRED height — which collapses (the WebView child is
    // MatchParent and contributes nothing to desired size). Fill when the
    // constraint is finite; never collapse to 0 on an infinite constraint (fall
    // back to base measurement). Logged so we can see what MAUI actually passes.
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        if (!double.IsInfinity(widthConstraint) && !double.IsInfinity(heightConstraint))
            return new Size(widthConstraint, heightConstraint);
        return base.GetDesiredSize(widthConstraint, heightConstraint);
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

        // Load terminal AFTER the view is laid out — WebView needs real dimensions
        // to render content. Loading in CreatePlatformView is too early.
        if (_webView != null)
        {
            _webView.ViewTreeObserver!.GlobalLayout += OnFirstLayout;
        }
    }

    private void OnFirstLayout(object? sender, EventArgs e)
    {
        if (_webView == null || _htmlLoaded) return;

        var w = _webView.Width;
        var h = _webView.Height;

        // Wait until the WebView actually has non-zero dimensions. GlobalLayout
        // fires repeatedly; load exactly once, when the view is genuinely sized.
        // (Previously this gave up after a single 200ms retry and loaded at 0x0,
        // so xterm.js rendered into a zero-height surface — nothing was visible.)
        if (w > 0 && h > 0)
        {
            _htmlLoaded = true;
            _webView.ViewTreeObserver!.GlobalLayout -= OnFirstLayout;
            _webView.LoadUrl("file:///android_asset/terminal.html");
            System.Diagnostics.Debug.WriteLine($"[SmartTerminal] Loaded terminal.html at {w}x{h}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[SmartTerminal] WebView not yet sized: {w}x{h}, waiting...");
        }
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        _termView = null;
        _webView?.Destroy();
        _webView = null;
        _inputOverlay = null;
        _extraKeysBar = null;
        _htmlLoaded = false;
        base.DisconnectHandler(platformView);
    }

    // --- C# → WebView ---

    private void WriteToTerminal(string data)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _webView?.EvaluateJavascript($"termWrite(\"{base64}\")", null);
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
    /// Applies sticky modifiers (Ctrl/Alt) from the extra keys bar, then forwards
    /// to xterm.js which handles the terminal emulation.
    /// </summary>
    private void OnSmartInput(string text)
    {
        var modified = _extraKeysBar?.ApplyModifiers(text) ?? text;
        _termView?.RaiseInputReceived(modified);
    }

    /// <summary>
    /// Called when a key on the extra keys bar is tapped (Esc, Tab, arrows, symbols).
    /// These bypass the soft keyboard entirely.
    /// </summary>
    private void OnExtraKeyInput(string sequence)
    {
        _termView?.RaiseInputReceived(sequence);
    }

    /// <summary>
    /// Called when the Paste button on the extra keys bar is tapped.
    /// Smart paste: detects clipboard content type.
    /// - Text → bracketed paste into terminal
    /// - Image → save to cache file, inject file path into terminal
    /// This enables pasting screenshots directly into Claude Code.
    /// </summary>
    private void OnPasteRequested()
    {
        try
        {
            var clipboard = (global::Android.Content.ClipboardManager?)
                Context?.GetSystemService(Context.ClipboardService);
            var clip = clipboard?.PrimaryClip;
            if (clip == null || clip.ItemCount == 0) return;

            var item = clip.GetItemAt(0);
            if (item == null) return;

            // Check for image content first
            var uri = item.Uri;
            if (uri != null && clip.Description != null)
            {
                for (int i = 0; i < clip.Description.MimeTypeCount; i++)
                {
                    var mime = clip.Description.GetMimeType(i);
                    if (mime != null && mime.StartsWith("image/"))
                    {
                        var path = SaveClipboardImage(uri, mime);
                        if (path != null)
                        {
                            // Inject the file path into the terminal
                            var safePath = SanitizePasteContent(path);
                            var bracketed = "\x1b[200~" + safePath + "\x1b[201~";
                            _termView?.RaiseInputReceived(bracketed);
                            return;
                        }
                    }
                }
            }

            // Fall back to text paste
            var text = item.CoerceToText(Context)?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                var safeText = SanitizePasteContent(text);
                var bracketed = "\x1b[200~" + safeText + "\x1b[201~";
                _termView?.RaiseInputReceived(bracketed);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
        }
    }

    /// <summary>
    /// TOP / END keys: jump to the top or bottom of the scrollback.
    /// </summary>
    private void OnScrollRequested(bool toTop)
    {
        _webView?.EvaluateJavascript(toTop ? "termScrollTop()" : "termScrollBottom()", null);
    }

    /// <summary>
    /// Called when the CPY button on the extra keys bar is tapped. xterm.js renders
    /// to canvas so Android text selection can't reach the terminal — instead we
    /// serialize the whole scrollback (termGetBuffer in terminal.html) and put it
    /// on the clipboard.
    /// </summary>
    private void OnCopyRequested()
    {
        _webView?.EvaluateJavascript("termGetBuffer()", new CopyBufferCallback(Context));
    }

    private sealed class CopyBufferCallback : Java.Lang.Object, global::Android.Webkit.IValueCallback
    {
        private readonly Context? _context;
        public CopyBufferCallback(Context? context) { _context = context; }

        public void OnReceiveValue(Java.Lang.Object? value)
        {
            try
            {
                // EvaluateJavascript delivers the JS return value JSON-encoded.
                string? json = value?.ToString();
                if (string.IsNullOrEmpty(json) || json == "null") return;
                string text = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
                if (text.Length == 0) return;

                var clipboard = (global::Android.Content.ClipboardManager?)
                    _context?.GetSystemService(Context.ClipboardService);
                var clip = global::Android.Content.ClipData.NewPlainText("terminal", text);
                if (clipboard != null && clip != null)
                {
                    clipboard.PrimaryClip = clip;
                    global::Android.Widget.Toast.MakeText(
                        _context, $"Copied {text.Length} chars", global::Android.Widget.ToastLength.Short)?.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Strips bracketed paste delimiters from content to prevent escape injection.
    /// A malicious clipboard containing \x1b[201~ could break out of bracketed paste mode.
    /// </summary>
    private static string SanitizePasteContent(string content)
    {
        return content
            .Replace("\x1b[200~", "")
            .Replace("\x1b[201~", "");
    }

    /// <summary>
    /// Saves a clipboard image URI to the app's cache directory.
    /// Returns the file path, or null on failure.
    /// </summary>
    private string? SaveClipboardImage(global::Android.Net.Uri uri, string mimeType)
    {
        try
        {
            var context = Context;
            if (context == null) return null;

            var resolver = context.ContentResolver;
            if (resolver == null) return null;

            // Determine file extension from MIME type
            var ext = mimeType switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".png"
            };

            var cacheDir = context.CacheDir?.AbsolutePath;
            if (cacheDir == null) return null;

            var fileName = $"clipboard_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var filePath = System.IO.Path.Combine(cacheDir, fileName);

            using var inputStream = resolver.OpenInputStream(uri);
            if (inputStream == null) return null;

            using var outputStream = System.IO.File.Create(filePath);
            inputStream.CopyTo(outputStream);

            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveClipboardImage error: {ex.Message}");
            return null;
        }
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
                    // xterm.js key event (for keys typed directly into xterm, not via
                    // our overlay) — route through OnSmartInput so sticky Ctrl/Alt from
                    // the extra keys bar apply on this path too.
                    OnSmartInput(data);
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

                case "bell":
                    // Terminal bell → vibrate
                    try
                    {
                        var vibrator = (global::Android.OS.Vibrator?)
                            Context?.GetSystemService(Context.VibratorService);
                        if (vibrator != null && vibrator.HasVibrator)
                        {
                            vibrator.Vibrate(global::Android.OS.VibrationEffect.CreateOneShot(
                                50, global::Android.OS.VibrationEffect.DefaultAmplitude));
                        }
                    }
                    catch { /* vibration not available */ }
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

    /// <summary>
    /// Prevents the WebView from navigating away from terminal.html.
    /// </summary>
    private class TerminalWebViewClient : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
            => true; // block all page-initiated navigation
    }

    /// <summary>
    /// Intercepts console.log messages from terminal.html for JS → C# communication.
    /// Messages prefixed with "smartterm:" are parsed and routed to HandleTerminalMessage.
    /// This replaces the unreliable window.location URL scheme interception.
    /// </summary>
    private class TerminalChromeClient : WebChromeClient
    {
        private readonly SmartTerminalHandler _handler;

        public TerminalChromeClient(SmartTerminalHandler handler) => _handler = handler;

        public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
        {
            var msg = consoleMessage?.Message();
            if (msg != null && msg.StartsWith("smartterm:"))
            {
                // Parse: smartterm:type:base64data
                var prefixLen = "smartterm:".Length;
                var secondColon = msg.IndexOf(':', prefixLen);
                if (secondColon > prefixLen)
                {
                    var type = msg.Substring(prefixLen, secondColon - prefixLen);
                    var data = msg.Substring(secondColon + 1);
                    _handler.HandleTerminalMessage(type, data);
                }
                return true;
            }
            return base.OnConsoleMessage(consoleMessage);
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
        SetRawInputType(InputTypes.ClassText | InputTypes.TextFlagAutoCorrect);
        Focusable = true;
        FocusableInTouchMode = true;
    }

    public override IInputConnection OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs != null)
        {
            outAttrs.InputType = InputTypes.ClassText | InputTypes.TextFlagAutoCorrect;
            outAttrs.ImeOptions = (ImeFlags)((int)ImeAction.None | 0x2000000);
        }
        return new SmartInputConnection(this, true, _onInput);
    }

    /// <summary>
    /// Physical/Bluetooth keyboards (and some IMEs) dispatch key events to the
    /// view directly, bypassing InputConnection.SendKeyEvent — without this
    /// override, Ctrl chords from a real keyboard land in the EditText buffer
    /// and never reach the terminal.
    /// </summary>
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (SmartInputConnection.TranslateKeyEvent(e, _onInput))
            return true;
        return base.OnKeyDown(keyCode, e);
    }

    /// <summary>
    /// Regain focus + soft keyboard whenever the app window becomes focused
    /// (launch, return from another app) — without this the operator must tap
    /// the terminal once or twice before typing.
    /// </summary>
    public override void OnWindowFocusChanged(bool hasWindowFocus)
    {
        base.OnWindowFocusChanged(hasWindowFocus);
        if (hasWindowFocus)
        {
            RequestFocus();
            // Post: IMM ignores ShowSoftInput while the window is still mid-focus-transition.
            Post(() =>
            {
                var imm = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
                imm?.ShowSoftInput(this, ShowFlags.Implicit);
            });
        }
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
        if (TranslateKeyEvent(e, _onInput))
            return true;
        return base.SendKeyEvent(e);
    }

    /// <summary>
    /// Shared key-event → terminal-bytes translation, used by both the
    /// InputConnection path (SendKeyEvent) and the view path (OnKeyDown).
    /// Returns true if the event was consumed.
    /// </summary>
    internal static bool TranslateKeyEvent(KeyEvent? e, Action<string> onInput)
    {
        if (e?.Action != KeyEventActions.Down)
            return false;

        switch (e.KeyCode)
        {
            case Keycode.Enter:
                onInput("\r");
                return true;

            case Keycode.Del:
                onInput("\x7f"); // DEL
                return true;

            case Keycode.Tab:
                onInput("\t");
                return true;

            case Keycode.Escape:
                onInput("\x1b");
                return true;

            // Arrow keys → ANSI escape sequences
            case Keycode.DpadUp:
                onInput("\x1b[A");
                return true;

            case Keycode.DpadDown:
                onInput("\x1b[B");
                return true;

            case Keycode.DpadRight:
                onInput("\x1b[C");
                return true;

            case Keycode.DpadLeft:
                onInput("\x1b[D");
                return true;

            default:
                // With Ctrl held, GetUnicodeChar() returns 0 on many devices —
                // derive the letter from the keycode so Ctrl-C still maps to 0x03.
                if (e.IsCtrlPressed && e.KeyCode >= Keycode.A && e.KeyCode <= Keycode.Z)
                {
                    onInput(((char)(e.KeyCode - Keycode.A + 1)).ToString());
                    return true;
                }

                // Regular character from physical keyboard
                var ch = (char)e.UnicodeChar;
                if (ch != 0)
                {
                    // Ctrl+letter → control byte (Ctrl-C = 0x03), for keyboards
                    // that send a real Ctrl chord
                    if (e.IsCtrlPressed)
                    {
                        var lower = char.ToLower(ch);
                        if (lower >= 'a' && lower <= 'z')
                        {
                            onInput(((char)(lower - 'a' + 1)).ToString());
                            return true;
                        }
                    }
                    onInput(ch.ToString());
                    return true;
                }
                break;
        }

        return false;
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
