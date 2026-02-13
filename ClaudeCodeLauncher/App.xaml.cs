using ClaudeCodeLauncher.Services;

namespace ClaudeCodeLauncher;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPreferencesService _preferencesService;

    public App(IServiceProvider serviceProvider, IPreferencesService preferencesService)
    {
        InitializeComponent();
        
        _serviceProvider = serviceProvider;
        _preferencesService = preferencesService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Check if setup is complete
        var isSetupComplete = _preferencesService.Get("SetupComplete", false);
        
        // Resolve pages from DI container
        Page startPage = isSetupComplete 
            ? _serviceProvider.GetRequiredService<MainPage>()
            : _serviceProvider.GetRequiredService<SetupPage>();

        return new Window(new NavigationPage(startPage));
    }
}
