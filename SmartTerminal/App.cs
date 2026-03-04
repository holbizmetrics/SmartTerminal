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
        return new Window(new NavigationPage(_terminalPage));
    }
}
