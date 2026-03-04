#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

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
    private readonly LinearLayout _container;

    private Button? _ctrlButton;
    private Button? _altButton;

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
        var result = input;

        if (_altState > 0 && input.Length == 1)
        {
            result = "\x1b" + input;
            if (_altState == 1) SetAltState(0);
        }

        if (_ctrlState > 0 && input.Length == 1)
        {
            var ch = char.ToLower(input[0]);
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

    public ExtraKeysBar(Context context, Action<string> onInput, Action onPaste) : base(context)
    {
        _onInput = onInput;
        _onPaste = onPaste;

        HorizontalScrollBarEnabled = false;
        SetBackgroundColor(Color.ParseColor("#0f0f23"));
        SetPadding(4, 2, 4, 2);

        _container = new LinearLayout(context)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal,
            LayoutParameters = new LayoutParams(LayoutParams.WrapContent, LayoutParams.MatchParent)
        };
        _container.SetGravity(GravityFlags.CenterVertical);

        // Build keys
        AddKey("ESC", "\x1b");
        _ctrlButton = AddModifierKey("CTR");
        _altButton = AddModifierKey("ALT");
        AddKey("TAB", "\t");
        AddKey("|", "|");
        AddKey("-", "-");
        AddKey("~", "~");
        AddKey("/", "/");
        AddKey("\\", "\\");
        AddKey("_", "_");
        AddKey("\u2191", "\x1b[A"); // Up arrow
        AddKey("\u2193", "\x1b[B"); // Down arrow
        AddKey("\u2190", "\x1b[D"); // Left arrow
        AddKey("\u2192", "\x1b[C"); // Right arrow
        AddPasteKey();

        AddView(_container);
    }

    private Button AddKey(string label, string output)
    {
        var btn = CreateButton(label);
        btn.Click += (s, e) => _onInput(output);
        _container.AddView(btn);
        return btn;
    }

    private Button AddModifierKey(string label)
    {
        var btn = CreateButton(label);
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
        var btn = CreateButton("PST");
        btn.Click += (s, e) => _onPaste();
        _container.AddView(btn);
    }

    private Button CreateButton(string label)
    {
        var ctx = Context!;
        var density = ctx.Resources!.DisplayMetrics!.Density;
        var btn = new Button(ctx);
        btn.Text = label;
        btn.SetTextColor(Color.ParseColor("#e0e0e0"));
        btn.TextSize = 12;
        btn.SetAllCaps(false);
        btn.SetMinimumWidth((int)(36 * density));
        btn.SetMinHeight((int)(36 * density));
        btn.SetPadding((int)(6 * density), 0, (int)(6 * density), 0);

        var lp = new LinearLayout.LayoutParams(LayoutParams.WrapContent, (int)(36 * density));
        lp.SetMargins((int)(2 * density), 0, (int)(2 * density), 0);
        btn.LayoutParameters = lp;

        // Flat dark style
        btn.SetBackgroundColor(Color.ParseColor("#1a1a2e"));
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

    private static void UpdateModifierVisual(Button? btn, int state)
    {
        if (btn == null) return;
        switch (state)
        {
            case 0: // Off
                btn.SetBackgroundColor(Color.ParseColor("#1a1a2e"));
                btn.SetTextColor(Color.ParseColor("#e0e0e0"));
                break;
            case 1: // Active (one-shot)
                btn.SetBackgroundColor(Color.ParseColor("#e94560"));
                btn.SetTextColor(Color.ParseColor("#ffffff"));
                break;
            case 2: // Locked
                btn.SetBackgroundColor(Color.ParseColor("#53b3cb"));
                btn.SetTextColor(Color.ParseColor("#ffffff"));
                break;
        }
    }
}
#endif
