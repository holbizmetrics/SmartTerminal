namespace SmartTerminal;

public class App : Application
{
    public App()
    {
        MainPage = new NavigationPage(new Views.TerminalPage());
    }
}
