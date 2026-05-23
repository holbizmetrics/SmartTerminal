namespace SmartTerminal.Services;

/// <summary>
/// Factory for creating PtyService instances — one per tab/session.
/// Replaces the singleton PtyService pattern for multi-tab support.
///
/// Usage:
///   var factory = serviceProvider.GetService&lt;IPtyServiceFactory&gt;();
///   var pty = factory.Create();
///   await pty.StartAsync("/bin/sh", 24, 80);
///   // ... use pty ...
///   pty.Dispose(); // when tab closes
/// </summary>
public interface IPtyServiceFactory
{
    /// <summary>Create a new PTY service instance for a tab.</summary>
    IPtyService Create();

    /// <summary>Get all active PTY sessions.</summary>
    IReadOnlyList<IPtyService> ActiveSessions { get; }

    /// <summary>Number of active sessions.</summary>
    int ActiveCount { get; }
}

public class PtyServiceFactory : IPtyServiceFactory, IDisposable
{
    private readonly List<IPtyService> _sessions = new();
    private readonly object _lock = new();
    private bool _disposed;

    public IPtyService Create()
    {
        // Pipe-based shell: managed process, no NDK/libpty. Runs commands + output.
        // Swap to `new PtyService()` once libpty.so is built for a full PTY.
        IPtyService pty = new PipeShellService();

        lock (_lock)
        {
            _sessions.Add(pty);
        }

        // Clean up when process exits
        pty.ProcessExited += (_) =>
        {
            lock (_lock)
            {
                _sessions.Remove(pty);
            }
        };

        return pty;
    }

    public IReadOnlyList<IPtyService> ActiveSessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Where(s => s.IsRunning).ToList().AsReadOnly();
            }
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count(s => s.IsRunning);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var pty in _sessions.ToList())
            {
                if (pty is IDisposable d)
                    d.Dispose();
            }
            _sessions.Clear();
        }

        GC.SuppressFinalize(this);
    }

    ~PtyServiceFactory() => Dispose();
}
