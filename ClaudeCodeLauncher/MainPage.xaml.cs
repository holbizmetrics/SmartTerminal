using ClaudeCodeLauncher.Services;

namespace ClaudeCodeLauncher;

public partial class MainPage : ContentPage
{
    private readonly ITermuxService _termuxService;
    private readonly IPreferencesService _preferencesService;
    private readonly IServiceProvider _serviceProvider;

    public MainPage(ITermuxService termuxService, IPreferencesService preferencesService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        _termuxService = termuxService;
        _preferencesService = preferencesService;
        _serviceProvider = serviceProvider;
        
        // Load saved working directory
        var savedDir = _preferencesService.Get("WorkingDirectory", "/storage/emulated/0");
        WorkingDirectoryEntry.Text = savedDir;
        
        // Check health on load
        _ = CheckHealthAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckHealthAsync();
    }

    private async Task CheckHealthAsync()
    {
        try
        {
            var health = await _termuxService.GetHealthCheckAsync();
            
            if (!health.TermuxInstalled)
            {
                StatusLabel.Text = "Termux not installed";
                StatusIndicator.BackgroundColor = Colors.Red;
            }
            else if (!health.ClaudeCodeInstalled)
            {
                StatusLabel.Text = "Claude Code not installed";
                StatusIndicator.BackgroundColor = Colors.Orange;
            }
            else
            {
                StatusLabel.Text = "Ready";
                StatusIndicator.BackgroundColor = Colors.Green;
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            StatusIndicator.BackgroundColor = Colors.Red;
        }
    }

    private async void OnLaunchClicked(object sender, EventArgs e)
    {
        try
        {
            LaunchButton.IsEnabled = false;
            LaunchButton.Text = "Launching...";

            var workDir = WorkingDirectoryEntry.Text;
            
            // Save working directory
            if (!string.IsNullOrWhiteSpace(workDir))
            {
                _preferencesService.Set("WorkingDirectory", workDir);
            }

            var success = await _termuxService.LaunchClaudeCodeAsync(workDir);
            
            if (!success)
            {
                await DisplayAlert("Error", "Failed to launch Claude Code. Is it installed?", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            LaunchButton.IsEnabled = true;
            LaunchButton.Text = "🚀 Launch Claude Code";
        }
    }

    private void OnCommandTextCommitted(object? sender, string text)
    {
        // Text was committed from predictive keyboard - it's already captured!
        System.Diagnostics.Debug.WriteLine($"Text committed: {text}");
    }

    private async void OnSpecialKeyPressed(object? sender, string key)
    {
        // Handle special key
        System.Diagnostics.Debug.WriteLine($"Special key: {key}");
        
        if (Enum.TryParse<SpecialKey>(key, out var specialKey))
        {
            await _termuxService.SendSpecialKeyAsync(specialKey);
        }
    }

    private async void OnSendCommandClicked(object sender, EventArgs e)
    {
        var command = CommandInput.Text;
        if (string.IsNullOrWhiteSpace(command)) return;

        await _termuxService.SendInputAsync(command);
        CommandInput.Clear();
    }

    private async void OnCtrlCClicked(object sender, EventArgs e)
    {
        await _termuxService.SendSpecialKeyAsync(SpecialKey.CtrlC);
    }

    private async void OnCtrlDClicked(object sender, EventArgs e)
    {
        await _termuxService.SendSpecialKeyAsync(SpecialKey.CtrlD);
    }

    private async void OnTabClicked(object sender, EventArgs e)
    {
        await _termuxService.SendSpecialKeyAsync(SpecialKey.Tab);
    }

    private async void OnEnterClicked(object sender, EventArgs e)
    {
        await _termuxService.SendSpecialKeyAsync(SpecialKey.Enter);
    }

    private async void OnOpenTermuxClicked(object sender, EventArgs e)
    {
        await _termuxService.OpenTermuxAsync();
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        // Navigate to settings page using DI
        var setupPage = _serviceProvider.GetRequiredService<SetupPage>();
        await Navigation.PushAsync(setupPage);
    }
}
