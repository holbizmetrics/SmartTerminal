using SmartTerminal.Views;

namespace SmartTerminal;

public class App : Application
{
    public App(TerminalPage terminalPage)
    {
        MainPage = new NavigationPage(terminalPage);
    }
}
