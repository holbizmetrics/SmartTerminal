using System.Diagnostics;
using System.Text;

namespace SmartTerminal.Services;

/// <summary>
/// Pipe-based shell. Spawns a shell (/system/bin/sh) as a normal child process
/// with redirected stdin/stdout/stderr — pure managed code, NO native library
/// and NO NDK required.
///
/// Capabilities: runs non-interactive commands (ls, echo, cat, printf incl.
/// OSC 1338 sequences) and streams their output back to the terminal.
///
/// Limitations vs a real PTY: there is no controlling TTY, so
///  - the shell does NOT echo input (EchoesInput = false; the UI echoes instead),
///  - interactive full-screen programs (vim, top, less) and job control / Ctrl+C
///    won't work, and isatty() is false.
/// Upgrade path: PtyService (libpty.so via NDK) provides the full PTY.
/// </summary>
public class PipeShellService : IPtyService, IDisposable
{
    private readonly object _lock = new();
    private Process? _proc;

    public bool NativeAvailable => true;   // no native dependency at all
    public bool EchoesInput => false;      // pipe has no TTY echo — UI must echo

    public bool IsRunning
    {
        get { lock (_lock) { return _proc is { HasExited: false }; } }
    }

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;

    public Task<bool> StartAsync(string shell = "/system/bin/sh", int rows = 24, int cols = 80)
    {
        try
        {
            var home = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = home,
            };
            psi.Environment["TERM"] = "xterm-256color";
            psi.Environment["HOME"] = home;
            psi.Environment["PATH"] = "/system/bin:/system/xbin:/vendor/bin";

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Exited += (_, _) =>
            {
                int code = 0;
                try { code = proc.ExitCode; } catch { /* ignore */ }
                ProcessExited?.Invoke(code);
            };

            if (!proc.Start())
                return Task.FromResult(false);

            lock (_lock) { _proc = proc; }

            PumpStream(proc.StandardOutput.BaseStream);
            PumpStream(proc.StandardError.BaseStream);
            return Task.FromResult(true);
        }
        catch (Exception e)
        {
            OutputReceived?.Invoke(
                $"\r\n\x1b[31m[pipe-shell failed to start {shell}: {e.Message}]\x1b[0m\r\n");
            return Task.FromResult(false);
        }
    }

    // Read raw bytes off a stream and forward to the terminal. Pipe output uses
    // bare \n; terminals need \r\n, so normalize.
    private void PumpStream(Stream stream)
    {
        _ = Task.Run(async () =>
        {
            var buf = new byte[4096];
            try
            {
                int n;
                while ((n = await stream.ReadAsync(buf.AsMemory(0, buf.Length))) > 0)
                {
                    var text = Encoding.UTF8.GetString(buf, 0, n);
                    text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    OutputReceived?.Invoke(text);
                }
            }
            catch { /* stream closed on exit */ }
        });
    }

    public async Task WriteAsync(string data)
    {
        Process? p;
        lock (_lock) { p = _proc; }
        if (p is { HasExited: false })
        {
            try
            {
                // Terminal sends \r on Enter; a line-reading shell wants \n.
                await p.StandardInput.WriteAsync(data.Replace("\r", "\n"));
                await p.StandardInput.FlushAsync();
            }
            catch { /* shell gone */ }
        }
    }

    public Task WriteAsync(byte[] data) => WriteAsync(Encoding.UTF8.GetString(data));

    public void Resize(int rows, int cols) { /* no TTY — nothing to resize */ }

    public void Stop()
    {
        Process? p;
        lock (_lock) { p = _proc; _proc = null; }
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
        try { p.Dispose(); } catch { /* ignore */ }
    }

    public void Dispose() => Stop();
}
