using SmartTerminal.Services;

namespace SmartTerminal.Views;

/// <summary>
/// Main page. Wires the terminal view to the PTY service:
///   Keyboard → SmartTerminalView → PtyService → shell
///   shell → PtyService → SmartTerminalView → xterm.js
/// </summary>
public class TerminalPage : ContentPage
{
    private readonly IPtyService _pty;
    private readonly SmartTerminalView _terminal;
    private bool _shellExited;

    public TerminalPage(IPtyService pty)
    {
        _pty = pty;

        BackgroundColor = Color.FromArgb("#1a1a2e");

        _terminal = new SmartTerminalView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // Terminal view events
        _terminal.InputReceived += OnInputReceived;
        _terminal.SizeChanged += OnTerminalSizeChanged;
        _terminal.TerminalReady += OnTerminalReady;

        // PTY events
        _pty.OutputReceived += OnPtyOutput;
        _pty.ProcessExited += OnProcessExited;

        Content = _terminal;
    }

    /// <summary>
    /// xterm.js is loaded and ready — start the shell.
    /// </summary>
    private async void OnTerminalReady(int cols, int rows)
    {
        var shell = FindShell();

        // Onboarding: show environment info on first launch
        _terminal.WriteOutput?.Invoke(
            "\x1b[36mSmart Terminal\x1b[0m\r\n" +
            $"Shell: {shell}\r\n");

        if (shell.Contains("termux"))
            _terminal.WriteOutput?.Invoke(
                "\x1b[32mTermux environment detected.\x1b[0m\r\n\r\n");
        else
            _terminal.WriteOutput?.Invoke(
                "\x1b[33mStock Android shell. Install Termux for full environment.\x1b[0m\r\n\r\n");

        bool started = await _pty.StartAsync(shell, rows, cols);

        // Start foreground service to keep session alive in background
        if (started)
        {
#if ANDROID
            SmartTerminal.Platforms.Android.Services.TerminalForegroundService.Start(
                Platform.CurrentActivity!);
#endif
        }

        if (!started)
        {
            if (!_pty.NativeAvailable)
            {
                _terminal.WriteOutput?.Invoke(
                    "\x1b[31mNative PTY library (libpty.so) not found.\x1b[0m\r\n" +
                    "The app was built without the native library.\r\n" +
                    "Run build_native.sh with Android NDK to build it.\r\n");
            }
            else
            {
                _terminal.WriteOutput?.Invoke(
                    "\x1b[31mFailed to start shell.\x1b[0m\r\n" +
                    $"Tried: {shell}\r\n" +
                    "Make sure a shell is available on this device.\r\n");
            }
        }
    }

    /// <summary>
    /// User typed something (via SwiftKey commitText or direct key event).
    /// Send it to the shell.
    /// </summary>
    private async void OnInputReceived(string data)
    {
        if (_pty.IsRunning)
        {
            await _pty.WriteAsync(data);
        }
        else if (_shellExited)
        {
            // Any keypress after exit restarts the shell
            _shellExited = false;
            _terminal.WriteOutput?.Invoke("\x1b[2J\x1b[H"); // Clear screen
            var shell = FindShell();
            await _pty.StartAsync(shell, 24, 80);
        }
    }

    /// <summary>
    /// Terminal view resized (keyboard show/hide, rotation).
    /// Inform the PTY so the shell adjusts its output.
    /// </summary>
    private void OnTerminalSizeChanged(int cols, int rows)
    {
        if (_pty.IsRunning)
        {
            _pty.Resize(rows, cols);
        }
    }

    /// <summary>
    /// Shell produced output — send it to xterm.js for rendering.
    /// </summary>
    private void OnPtyOutput(string data)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _terminal.WriteOutput?.Invoke(data);
        });
    }

    /// <summary>
    /// Shell process exited — show message and offer restart.
    /// </summary>
    private void OnProcessExited(int exitCode)
    {
        _shellExited = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _terminal.WriteOutput?.Invoke(
                $"\r\n\x1b[33m[Process exited with code {exitCode}]\x1b[0m\r\n" +
                "\x1b[90mPress any key to restart...\x1b[0m\r\n");
        });
    }

    /// <summary>
    /// Find a working shell on this Android device.
    /// Priority: bash → sh → system sh.
    /// </summary>
    private static string FindShell()
    {
        // If we have a Termux-like environment with bash
        string[] candidates = {
            "/data/data/com.termux/files/usr/bin/bash",
            "/data/data/com.termux/files/usr/bin/sh",
            "/system/bin/sh",
            "/bin/sh"
        };

        foreach (var shell in candidates)
        {
            if (File.Exists(shell))
                return shell;
        }

        return "/system/bin/sh";
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Unsubscribe to prevent handler accumulation on the singleton PtyService
        _terminal.InputReceived -= OnInputReceived;
        _terminal.SizeChanged -= OnTerminalSizeChanged;
        _terminal.TerminalReady -= OnTerminalReady;
        _pty.OutputReceived -= OnPtyOutput;
        _pty.ProcessExited -= OnProcessExited;

        _pty.Stop();

        // Stop foreground service when terminal page disappears
#if ANDROID
        SmartTerminal.Platforms.Android.Services.TerminalForegroundService.Stop(
            Platform.CurrentActivity!);
#endif
    }
}
