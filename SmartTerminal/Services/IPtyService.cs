namespace SmartTerminal.Services;

/// <summary>
/// Manages a pseudo-terminal (PTY) session.
/// Spawns a shell, reads output, writes input.
/// </summary>
public interface IPtyService
{
    /// <summary>Start a shell session. Returns false if PTY allocation fails.</summary>
    Task<bool> StartAsync(string shell = "/bin/sh", int rows = 24, int cols = 80);

    /// <summary>Write raw bytes to the PTY (keyboard input → shell stdin).</summary>
    Task WriteAsync(string data);

    /// <summary>Write raw bytes to the PTY.</summary>
    Task WriteAsync(byte[] data);

    /// <summary>Resize the PTY window.</summary>
    void Resize(int rows, int cols);

    /// <summary>Kill the shell process and close the PTY.</summary>
    void Stop();

    /// <summary>True if the shell process is running.</summary>
    bool IsRunning { get; }

    /// <summary>Whether the native PTY library is available. False if libpty.so is missing.</summary>
    bool NativeAvailable { get; }

    /// <summary>
    /// True if the shell echoes typed input itself (a real PTY in cooked mode does).
    /// False for pipe-based shells with no TTY — the UI must echo input locally.
    /// </summary>
    bool EchoesInput { get; }

    /// <summary>Fired when the shell produces output (stdout + stderr merged, as real terminals do).</summary>
    event Action<string>? OutputReceived;

    /// <summary>Fired when the shell process exits.</summary>
    event Action<int>? ProcessExited;
}
