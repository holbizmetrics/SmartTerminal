using SmartTerminal.Services;

namespace SmartTerminal.Views;

/// <summary>
/// Multi-tab terminal page. Each tab is an independent TerminalPage
/// with its own PTY session via PtyServiceFactory.
///
/// Tab bar at bottom with + button to add new tabs.
/// Swipe or tap to switch. Long-press to close.
/// </summary>
public class TabbedTerminalPage : ContentPage
{
    private readonly IPtyServiceFactory _factory;
    private readonly List<TabSession> _sessions = new();
    private int _activeIndex = -1;
    private readonly Grid _rootLayout;
    private readonly HorizontalStackLayout _tabBar;
    private readonly ContentView _terminalContainer;
    private int _tabCounter = 0;

    private class TabSession
    {
        public string Title { get; set; } = "";
        public IPtyService Pty { get; set; } = null!;
        public SmartTerminalView Terminal { get; set; } = null!;
        public Button TabButton { get; set; } = null!;
    }

    public TabbedTerminalPage(IPtyServiceFactory factory)
    {
        _factory = factory;
        BackgroundColor = Color.FromArgb("#1a1a2e");

        // Root layout: terminal on top, tab bar at bottom
        _rootLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),    // terminal
                new RowDefinition(new GridLength(40)),  // tab bar
            },
        };

        // Terminal container
        _terminalContainer = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        Grid.SetRow(_terminalContainer, 0);
        _rootLayout.Add(_terminalContainer);

        // Tab bar
        var tabBarScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#0f0f23"),
        };

        _tabBar = new HorizontalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(4, 2),
        };

        // "+" button
        var addButton = new Button
        {
            Text = "+",
            FontSize = 18,
            WidthRequest = 40,
            HeightRequest = 36,
            BackgroundColor = Color.FromArgb("#2a2a4a"),
            TextColor = Color.FromArgb("#53b3cb"),
            CornerRadius = 4,
            Padding = 0,
        };
        addButton.Clicked += (s, e) => AddTab();
        _tabBar.Add(addButton);

        tabBarScroll.Content = _tabBar;
        Grid.SetRow(tabBarScroll, 1);
        _rootLayout.Add(tabBarScroll);

        Content = _rootLayout;

        // Start with one tab
        AddTab();
    }

    private void AddTab()
    {
        _tabCounter++;
        var title = $"Term {_tabCounter}";

        var pty = _factory.Create();
        var terminal = new SmartTerminalView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        var tabButton = new Button
        {
            Text = title,
            FontSize = 12,
            HeightRequest = 36,
            MinimumWidthRequest = 80,
            BackgroundColor = Color.FromArgb("#1a1a3e"),
            TextColor = Color.FromArgb("#e0e0e0"),
            CornerRadius = 4,
            Padding = new Thickness(8, 0),
        };

        var session = new TabSession
        {
            Title = title,
            Pty = pty,
            Terminal = terminal,
            TabButton = tabButton,
        };

        var index = _sessions.Count;
        _sessions.Add(session);

        // Tab click → switch
        tabButton.Clicked += (s, e) => SwitchToTab(index);

        // Insert before the "+" button
        _tabBar.Insert(_tabBar.Count - 1, tabButton);

        // Wire terminal ↔ PTY
        terminal.InputReceived += async (data) =>
        {
            if (pty.IsRunning)
                await pty.WriteAsync(data);
        };

        terminal.SizeChanged += (cols, rows) =>
        {
            if (pty.IsRunning)
                pty.Resize(rows, cols);
        };

        terminal.TerminalReady += async (cols, rows) =>
        {
            var shell = FindShell();
            terminal.WriteOutput?.Invoke(
                $"\x1b[36m{title}\x1b[0m — {shell}\r\n\r\n");
            await pty.StartAsync(shell, rows, cols);
        };

        pty.OutputReceived += (data) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                terminal.WriteOutput?.Invoke(data);
            });
        };

        pty.ProcessExited += (code) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                terminal.WriteOutput?.Invoke(
                    $"\r\n\x1b[33m[Tab '{title}' exited: {code}]\x1b[0m\r\n");
                tabButton.TextColor = Color.FromArgb("#666666");
            });
        };

        // Switch to new tab
        SwitchToTab(index);
    }

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _sessions.Count) return;

        // Deactivate old tab
        if (_activeIndex >= 0 && _activeIndex < _sessions.Count)
        {
            _sessions[_activeIndex].TabButton.BackgroundColor = Color.FromArgb("#1a1a3e");
        }

        // Activate new tab
        _activeIndex = index;
        var session = _sessions[index];
        session.TabButton.BackgroundColor = Color.FromArgb("#e94560");
        _terminalContainer.Content = session.Terminal;
    }

    private static string FindShell()
    {
        string[] candidates = {
            "/data/data/com.termux/files/usr/bin/bash",
            "/data/data/com.termux/files/usr/bin/sh",
            "/system/bin/sh",
            "/bin/sh"
        };
        foreach (var shell in candidates)
        {
            if (File.Exists(shell)) return shell;
        }
        return "/system/bin/sh";
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        foreach (var session in _sessions)
        {
            session.Pty.Stop();
            if (session.Pty is IDisposable d) d.Dispose();
        }
        _sessions.Clear();
    }
}
