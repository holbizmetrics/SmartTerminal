namespace SmartTerminal.Views;

/// <summary>
/// Cross-platform terminal view.
/// On Android, this gets mapped to SmartTerminalHandler which uses
/// a WebView + SmartInputConnection to render xterm.js with full
/// predictive keyboard support.
/// </summary>
public class SmartTerminalView : View
{
    /// <summary>Write shell output to the terminal display.</summary>
    public Action<string>? WriteOutput { get; set; }

    /// <summary>Resize the terminal grid.</summary>
    public Action<int, int>? ResizeTerminal { get; set; }

    /// <summary>Fit terminal to current container size.</summary>
    public Action? FitTerminal { get; set; }

    /// <summary>Focus the terminal (show keyboard).</summary>
    public Action? FocusTerminal { get; set; }

    // --- Events from the terminal view to the page ---

    /// <summary>Fired when the user types (key events from xterm.js or commitText from SwiftKey).</summary>
    public event Action<string>? InputReceived;

    /// <summary>Fired when terminal reports a resize.</summary>
    public new event Action<int, int>? SizeChanged;

    /// <summary>Fired when the xterm.js terminal is initialized and ready.</summary>
    public event Action<int, int>? TerminalReady;

    // --- Internal: called by the handler ---

    internal void RaiseInputReceived(string data) => InputReceived?.Invoke(data);
    internal void RaiseSizeChanged(int cols, int rows) => SizeChanged?.Invoke(cols, rows);
    internal void RaiseTerminalReady(int cols, int rows) => TerminalReady?.Invoke(cols, rows);
}
