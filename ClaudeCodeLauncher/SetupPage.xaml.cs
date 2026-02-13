using ClaudeCodeLauncher.Services;

namespace ClaudeCodeLauncher;

public partial class SetupPage : ContentPage
{
    private readonly ITermuxService _termuxService;
    private readonly IPreferencesService _preferencesService;
    private readonly IServiceProvider _serviceProvider;

    public SetupPage(ITermuxService termuxService, IPreferencesService preferencesService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        _termuxService = termuxService;
        _preferencesService = preferencesService;
        _serviceProvider = serviceProvider;
        
        _ = CheckRequirementsAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckRequirementsAsync();
    }

    private async Task CheckRequirementsAsync()
    {
        try
        {
            var health = await _termuxService.GetHealthCheckAsync();
            
            TermuxCheck.Text = health.TermuxInstalled ? "✅" : "❌";
            NodeCheck.Text = health.NodeInstalled ? "✅" : "⏳";
            GitCheck.Text = health.GitInstalled ? "✅" : "⏳";
            ClaudeCheck.Text = health.ClaudeCodeInstalled ? "✅" : "⏳";
            
            // If everything is installed, show skip option more prominently
            if (health.TermuxInstalled && health.NodeInstalled && health.ClaudeCodeInstalled)
            {
                InstallButton.Text = "✅ All requirements met - Continue";
                InstallButton.BackgroundColor = Colors.Green;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to check requirements: {ex.Message}", "OK");
        }
    }

    private async void OnInstallClicked(object sender, EventArgs e)
    {
        // Check if Termux is installed first
        if (!await _termuxService.IsTermuxInstalledAsync())
        {
            var install = await DisplayAlert(
                "Termux Required",
                "Termux is not installed. Would you like to open F-Droid to install it?",
                "Open F-Droid",
                "Cancel");
                
            if (install)
            {
                await Launcher.OpenAsync("https://f-droid.org/packages/com.termux/");
            }
            return;
        }

        // Start setup
        InstallButton.IsEnabled = false;
        ManualButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        ProgressFrame.IsVisible = true;

        var progress = new Progress<string>(message =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressLabel.Text = message;
                LogOutput.Text += message + "\n";
                
                // Scroll to bottom
                // LogOutput.CursorPosition = LogOutput.Text.Length;
            });
        });

        try
        {
            var result = await _termuxService.RunSetupAsync(progress);
            
            SetupProgress.Progress = 1.0;
            
            if (result.Success)
            {
                await DisplayAlert("Success", "Setup completed successfully!", "Continue");
                _preferencesService.Set("SetupComplete", true);
                
                // Navigate to main page using DI
                var mainPage = _serviceProvider.GetRequiredService<MainPage>();
                Application.Current!.Windows[0].Page = new NavigationPage(mainPage);
            }
            else
            {
                await DisplayAlert("Setup Failed", result.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            InstallButton.IsEnabled = true;
            ManualButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
        }
    }

    private async void OnManualClicked(object sender, EventArgs e)
    {
        var instructions = @"
Manual Setup Instructions:

1. Install Termux from F-Droid (NOT Play Store)
   https://f-droid.org/packages/com.termux/

2. Open Termux and run:
   pkg update && pkg upgrade -y
   pkg install -y nodejs-lts git
   npm install -g @anthropic-ai/claude-code
   termux-setup-storage

3. (Android 12+) Disable Phantom Process Killer:
   - Enable Developer Options
   - Go to Developer Options
   - Find 'Disable child process restrictions'
   - Turn it ON

4. Return here and tap 'Skip'
";

        await DisplayAlert("Manual Setup", instructions, "OK");
    }

    private async void OnSkipClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Skip Setup?",
            "Make sure you have Termux with Node.js and Claude Code installed.",
            "Yes, I'm ready",
            "Cancel");

        if (confirm)
        {
            _preferencesService.Set("SetupComplete", true);
            var mainPage = _serviceProvider.GetRequiredService<MainPage>();
            Application.Current!.Windows[0].Page = new NavigationPage(mainPage);
        }
    }

    private async void OnLearnMoreClicked(object sender, EventArgs e)
    {
        var info = @"
Android 12+ Phantom Process Killer

Android 12 introduced aggressive background process management that can kill Termux processes.

To fix:
1. Enable Developer Options (tap Build Number 7 times in Settings > About)
2. Go to Settings > Developer Options
3. Find 'Disable child process restrictions' or similar
4. Turn it ON

Alternative (requires ADB):
adb shell device_config set_sync_disabled_for_tests persistent
adb shell device_config put activity_manager max_phantom_processes 2147483647

This allows Claude Code to run without being killed.
";

        await DisplayAlert("Phantom Process Killer", info, "OK");
    }
}
