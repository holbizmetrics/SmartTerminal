#if ANDROID
using Android.Content;
using Android.Views;
using Android.Widget;
using AButton = Android.Widget.Button;
using AColor = Android.Graphics.Color;

namespace SmartTerminal.Platforms.Android.Handlers;

/// <summary>
/// Horizontal scrollable toolbar with terminal extra keys.
/// Keys: Esc, Ctrl (sticky), Alt (sticky), Tab, |, -, ~, /, \, _, arrows, Paste.
/// Ctrl/Alt have three states: OFF → ACTIVE (one-shot) → LOCKED (stays on).
/// </summary>
internal class ExtraKeysBar : HorizontalScrollView
{
    private readonly Action<string> _onInput;
    private readonly Action _onPaste;
    private readonly Action _onCopy;
    private readonly Action<bool> _onScroll; // true = to top, false = to bottom
    private readonly LinearLayout _container;

    private AButton? _ctrlButton;
    private AButton? _altButton;

    // Modifier states: 0 = off, 1 = active (one-shot), 2 = locked
    private int _ctrlState;
    private int _altState;

    public bool CtrlActive => _ctrlState > 0;
    public bool AltActive => _altState > 0;

    /// <summary>
    /// Call after a key is sent through the normal input path to apply
    /// and auto-deactivate one-shot modifiers.
    /// </summary>
    public string ApplyModifiers(string input)
    {
        // Predictive keyboards (SwiftKey/Gboard) commit "c " or a word instead of
        // a single char — with a modifier armed, a trimmed single letter is the key,
        // otherwise CTR+c silently sends "c " and Ctrl-C never reaches the shell.
        var key = input;
        if ((_ctrlState > 0 || _altState > 0) && input.Length > 1)
        {
            var trimmed = input.Trim();
            if (trimmed.Length == 1) key = trimmed;
        }

        var result = key;

        if (_altState > 0 && key.Length == 1)
        {
            result = "\x1b" + key;
            if (_altState == 1) SetAltState(0);
        }

        if (_ctrlState > 0 && key.Length == 1)
        {
            var ch = char.ToLower(key[0]);
            if (ch >= 'a' && ch <= 'z')
            {
                result = ((char)(ch - 'a' + 1)).ToString();
            }
            if (_ctrlState == 1) SetCtrlState(0);
        }

        return result;
    }

    /// <summary>Reset one-shot modifiers after a key event (call if modifier was applied externally).</summary>
    public void ConsumeOneShot()
    {
        if (_ctrlState == 1) SetCtrlState(0);
        if (_altState == 1) SetAltState(0);
    }

    public ExtraKeysBar(Context context, Action<string> onInput, Action onPaste, Action onCopy, Action<bool> onScroll) : base(context)
    {
        _onInput = onInput;
        _onPaste = onPaste;
        _onCopy = onCopy;
        _onScroll = onScroll;

        // "There's more here" hints: a fading edge on the side that has more
        // content (the strongest native affordance) + a persistent thin
        // scrollbar. Both users (operator AND the agent) missed that this bar
        // scrolls — the arrow/PST/CPY keys were invisible for two days (2026-07-07).
        HorizontalScrollBarEnabled = true;
        ScrollbarFadingEnabled = false;
        HorizontalFadingEdgeEnabled = true;
        SetFadingEdgeLength((int)(24 * context.Resources!.DisplayMetrics!.Density));
        SetBackgroundColor(AColor.ParseColor("#0f0f23"));
        SetPadding(4, 2, 4, 2);

        _container = new LinearLayout(context)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal,
            LayoutParameters = new LayoutParams(LayoutParams.WrapContent, LayoutParams.MatchParent)
        };
        _container.SetGravity(GravityFlags.CenterVertical);

        // Build keys \u2014 DAILY keys first so they are visible without scrolling
        // (~9-10 keys fit on a phone width; the bar scrolls for the rest).
        // Arrows lead: Claude Code menu navigation + shell history live there,
        // and they were parked off-screen at positions 11-14 until 2026-07-28.
        AddKey("ESC", "\x1b");
        AddKey("TAB", "\t");
        AddRepeatingKey("\u2191", "\x1b[A"); // Up arrow    (hold to repeat)
        AddRepeatingKey("\u2193", "\x1b[B"); // Down arrow  (hold to repeat)
        AddRepeatingKey("\u2190", "\x1b[D"); // Left arrow  (hold to repeat)
        AddRepeatingKey("\u2192", "\x1b[C"); // Right arrow (hold to repeat)
        _ctrlButton = AddModifierKey("CTR");
        _altButton = AddModifierKey("ALT");
        AddPasteKey();
        AddCopyKey();
        AddKey("|", "|");
        AddKey("-", "-");
        AddKey("~", "~");
        AddKey("/", "/");
        AddKey("\\", "\\");
        AddKey("_", "_");
        AddScrollKeys();

        AddView(_container);
    }

    private AButton AddKey(string label, string output)
    {
        var btn = CreateKeyButton(label);
        btn.Click += (s, e) => _onInput(output);
        _container.AddView(btn);
        return btn;
    }

    /// <summary>
    /// Key that auto-repeats while held (Termux-style): first repeat after
    /// 400ms, then every 80ms. Tap still sends exactly one key via Click.
    /// </summary>
    private AButton AddRepeatingKey(string label, string output)
    {
        var btn = CreateKeyButton(label);
        var handler = new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!);
        Java.Lang.IRunnable? repeat = null;
        var repeating = false;

        repeat = new Java.Lang.Runnable(() =>
        {
            if (!repeating) return;
            _onInput(output);
            handler.PostDelayed(repeat!, RepeatIntervalMs);
        });

        btn.Click += (s, e) =>
        {
            // Suppress the synthetic click at the end of a hold — the repeats
            // already sent the key; one more on release would overshoot.
            if (!_suppressNextClick.Remove(btn))
                _onInput(output);
        };

        btn.Touch += (s, e) =>
        {
            switch (e.Event?.Action)
            {
                case MotionEventActions.Down:
                    repeating = true;
                    handler.PostDelayed(new Java.Lang.Runnable(() =>
                    {
                        if (repeating)
                        {
                            _suppressNextClick.Add(btn);
                            _onInput(output);               // first held repeat
                            handler.PostDelayed(repeat!, RepeatIntervalMs);
                        }
                    }), RepeatDelayMs);
                    break;
                case MotionEventActions.Up:
                    repeating = false;
                    break;
                case MotionEventActions.Cancel:
                    // No Click follows a Cancel — a stale suppress entry would
                    // swallow the NEXT tap on this key.
                    repeating = false;
                    _suppressNextClick.Remove(btn);
                    break;
            }
            e.Handled = false; // keep normal button behavior (pressed state, Click)
        };

        _container.AddView(btn);
        return btn;
    }

    private const int RepeatDelayMs = 400;
    private const int RepeatIntervalMs = 80;
    private readonly HashSet<AButton> _suppressNextClick = new();

    private AButton AddModifierKey(string label)
    {
        var btn = CreateKeyButton(label);
        btn.Click += (s, e) =>
        {
            if (label == "CTR")
            {
                SetCtrlState((_ctrlState + 1) % 3);
            }
            else
            {
                SetAltState((_altState + 1) % 3);
            }
        };
        _container.AddView(btn);
        return btn;
    }

    private void AddPasteKey()
    {
        var btn = CreateKeyButton("PST");
        btn.Click += (s, e) => _onPaste();
        _container.AddView(btn);
    }

    private void AddCopyKey()
    {
        var btn = CreateKeyButton("CPY");
        btn.Click += (s, e) => _onCopy();
        _container.AddView(btn);
    }

    private void AddScrollKeys()
    {
        var top = CreateKeyButton("TOP");
        top.Click += (s, e) => _onScroll(true);
        _container.AddView(top);

        var end = CreateKeyButton("END");
        end.Click += (s, e) => _onScroll(false);
        _container.AddView(end);
    }

    private AButton CreateKeyButton(string label)
    {
        var ctx = Context!;
        var density = ctx.Resources!.DisplayMetrics!.Density;
        var btn = new AButton(ctx);
        btn.Text = label;
        btn.SetTextColor(AColor.ParseColor("#e0e0e0"));
        btn.TextSize = 12;
        btn.SetAllCaps(false);
        btn.SetMinimumWidth((int)(36 * density));
        btn.SetMinHeight((int)(36 * density));
        btn.SetPadding((int)(6 * density), 0, (int)(6 * density), 0);

        var lp = new LinearLayout.LayoutParams(LayoutParams.WrapContent, (int)(36 * density));
        lp.SetMargins((int)(2 * density), 0, (int)(2 * density), 0);
        btn.LayoutParameters = lp;

        // Flat dark style
        btn.SetBackgroundColor(AColor.ParseColor("#1a1a2e"));
        btn.StateListAnimator = null; // Remove elevation shadow

        return btn;
    }

    private void SetCtrlState(int state)
    {
        _ctrlState = state;
        UpdateModifierVisual(_ctrlButton, state);
    }

    private void SetAltState(int state)
    {
        _altState = state;
        UpdateModifierVisual(_altButton, state);
    }

    private static void UpdateModifierVisual(AButton? btn, int state)
    {
        if (btn == null) return;
        switch (state)
        {
            case 0: // Off
                btn.SetBackgroundColor(AColor.ParseColor("#1a1a2e"));
                btn.SetTextColor(AColor.ParseColor("#e0e0e0"));
                break;
            case 1: // Active (one-shot)
                btn.SetBackgroundColor(AColor.ParseColor("#e94560"));
                btn.SetTextColor(AColor.ParseColor("#ffffff"));
                break;
            case 2: // Locked
                btn.SetBackgroundColor(AColor.ParseColor("#53b3cb"));
                btn.SetTextColor(AColor.ParseColor("#ffffff"));
                break;
        }
    }
}
#endif
