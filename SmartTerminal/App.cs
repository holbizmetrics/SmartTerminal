using SmartTerminal.Views;

namespace SmartTerminal;

public class App : Application
{
    private readonly TerminalPage _terminalPage;

    public App(TerminalPage terminalPage)
    {
        _terminalPage = terminalPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // No NavigationPage — it adds a nav bar/hamburger that covers terminal content
        return new Window(_terminalPage);
    }
}
