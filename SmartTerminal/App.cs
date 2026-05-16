using SmartTerminal.Views;

namespace SmartTerminal;

public class App : Application
{
    private readonly TabbedTerminalPage _tabbedPage;

    public App(TabbedTerminalPage tabbedPage)
    {
        _tabbedPage = tabbedPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Multi-tab terminal — no NavigationPage wrapper
        return new Window(_tabbedPage);
    }
}
