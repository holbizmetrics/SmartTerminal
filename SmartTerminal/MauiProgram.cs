using SmartTerminal.Platforms.Android.Handlers;
using SmartTerminal.Services;
using SmartTerminal.Views;

namespace SmartTerminal;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("CascadiaMono.ttf", "CascadiaMono");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler<SmartTerminalView, SmartTerminalHandler>();
#endif
            });

        // Register services
        builder.Services.AddSingleton<IPtyService, PtyService>(); // backward compat: single-tab
        builder.Services.AddSingleton<IPtyServiceFactory, PtyServiceFactory>(); // multi-tab ready
        builder.Services.AddTransient<TerminalPage>();

        return builder.Build();
    }
}
