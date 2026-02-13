using ClaudeCodeLauncher.Services;
using ClaudeCodeLauncher.Views;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

#if ANDROID
using ClaudeCodeLauncher.Platforms.Android.Handlers;
#endif

namespace ClaudeCodeLauncher;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                // OpenSans fonts - download from Google Fonts if missing:
                // https://fonts.google.com/specimen/Open+Sans
                // Place in Resources/Fonts/
                try
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                }
                catch
                {
                    // Fonts not found - app will use system default
                    System.Diagnostics.Debug.WriteLine("OpenSans fonts not found. Using system fonts.");
                }
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                // Register SmartInput handler for SwiftKey-compatible input
                handlers.AddHandler<SmartInput, SmartInputHandler>();
#endif
            });

        // Register services
        builder.Services.AddSingleton<ITermuxService, TermuxService>();
        builder.Services.AddSingleton<IPreferencesService, PreferencesService>();
        
        // Register pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<SetupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
