using System.Runtime.InteropServices;
using System.Text;

namespace SmartTerminal.Services;

/// <summary>
/// PTY service backed by native libpty.so.
/// Allocates a real pseudo-terminal via forkpty(), reads/writes through the master fd.
/// </summary>
public class PtyService : IPtyService, IDisposable
{
    // --- Native interop ---

    private const string LibName = "pty";

    [DllImport(LibName, EntryPoint = "pty_open")]
    private static extern int NativeOpen(int rows, int cols, string shell, out int masterFd, out int pid);

    [DllImport(LibName, EntryPoint = "pty_read")]
    private static extern int NativeRead(int masterFd, byte[] buffer, int bufferSize);

    [DllImport(LibName, EntryPoint = "pty_write")]
    private static extern int NativeWrite(int masterFd, byte[] data, int length);

    [DllImport(LibName, EntryPoint = "pty_resize")]
    private static extern int NativeResize(int masterFd, int rows, int cols);

    [DllImport(LibName, EntryPoint = "pty_close")]
    private static extern void NativeClose(int masterFd);

    [DllImport(LibName, EntryPoint = "pty_kill")]
    private static extern void NativeKill(int pid);

    [DllImport(LibName, EntryPoint = "pty_waitpid")]
    private static extern int NativeWaitPid(int pid);

    // --- State ---

    private readonly object _lock = new();
    private int _masterFd = -1;
    private int _pid = -1;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private Task? _waitTask;
    private bool _disposed;
    private bool _nativeAvailable = true;
    private Decoder? _utf8Decoder;

    /// <summary>Whether the native PTY library was found. False after DllNotFoundException.</summary>
    public bool NativeAvailable => _nativeAvailable;

    // A real PTY runs in cooked mode and echoes input itself.
    public bool EchoesInput => true;

    public bool IsRunning => _pid > 0 && _masterFd >= 0;

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;

    public Task<bool> StartAsync(string shell = "/bin/sh", int rows = 24, int cols = 80)
    {
        lock (_lock)
        {
            if (IsRunning)
                Stop();

            int result;
            try
            {
                result = NativeOpen(rows, cols, shell, out _masterFd, out _pid);
            }
            catch (DllNotFoundException)
            {
                _nativeAvailable = false;
                _masterFd = -1;
                _pid = -1;
                return Task.FromResult(false);
            }

            if (result != 0 || _masterFd < 0)
            {
                _masterFd = -1;
                _pid = -1;
                return Task.FromResult(false);
            }

            // Start background read loop
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCts.Token));

            // Start background wait for process exit
            _waitTask = Task.Run(() => WaitForExit());

            return Task.FromResult(true);
        }
    }

    public Task WriteAsync(string data)
    {
        int fd;
        lock (_lock)
        {
            if (!IsRunning) return Task.CompletedTask;
            fd = _masterFd;
        }
        var bytes = Encoding.UTF8.GetBytes(data);
        NativeWrite(fd, bytes, bytes.Length);
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data)
    {
        int fd;
        lock (_lock)
        {
            if (!IsRunning) return Task.CompletedTask;
            fd = _masterFd;
        }
        NativeWrite(fd, data, data.Length);
        return Task.CompletedTask;
    }

    public void Resize(int rows, int cols)
    {
        int fd;
        lock (_lock)
        {
            if (!IsRunning) return;
            fd = _masterFd;
        }
        NativeResize(fd, rows, cols);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_pid > 0)
            {
                NativeKill(_pid);
                _pid = -1;
            }

            _readCts?.Cancel();

            if (_masterFd >= 0)
            {
                NativeClose(_masterFd);
                _masterFd = -1;
            }

            _readCts?.Dispose();
            _readCts = null;
        }
    }

    private void ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        // Decoder maintains state across reads to handle split multi-byte UTF-8 chars
        _utf8Decoder = Encoding.UTF8.GetDecoder();
        var charBuffer = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];

        try
        {
            while (!ct.IsCancellationRequested && _masterFd >= 0)
            {
                int bytesRead = NativeRead(_masterFd, buffer, buffer.Length);

                if (bytesRead <= 0)
                {
                    // EOF or error — shell closed
                    break;
                }

                int charCount = _utf8Decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0);
                var text = new string(charBuffer, 0, charCount);
                OutputReceived?.Invoke(text);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"ReadLoop error: {ex.Message}");
        }
    }

    private void WaitForExit()
    {
        if (_pid <= 0) return;
        int exitCode = NativeWaitPid(_pid);
        _pid = -1;
        ProcessExited?.Invoke(exitCode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    ~PtyService() => Dispose();
}
