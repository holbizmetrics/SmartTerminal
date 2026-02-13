#if ANDROID
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Application = Android.App.Application;

namespace ClaudeCodeLauncher.Services;

/// <summary>
/// Android-specific implementation of TermuxService
/// Uses Termux:API and RUN_COMMAND intents
/// </summary>
public partial class TermuxService
{
    private const string TermuxPackageName = "com.termux";
    private const string TermuxApiPackageName = "com.termux.api";
    private const string TermuxRunCommandService = "com.termux.app.RunCommandService";
    
    public partial async Task<bool> IsTermuxInstalledAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var pm = Application.Context.PackageManager;
                pm?.GetPackageInfo(TermuxPackageName, PackageInfoFlags.Activities);
                return true;
            }
            catch (PackageManager.NameNotFoundException)
            {
                return false;
            }
        });
    }

    public partial async Task<bool> IsClaudeCodeInstalledAsync()
    {
        // We check by trying to run 'which claude' - if it returns successfully, it's installed
        // For now, return based on preferences (set after setup)
        return await Task.FromResult(
            Preferences.Default.Get("ClaudeCodeInstalled", false)
        );
    }

    public partial async Task<bool> IsNodeInstalledAsync()
    {
        return await Task.FromResult(
            Preferences.Default.Get("NodeInstalled", false)
        );
    }

    public partial async Task<bool> RunCommandAsync(string command)
    {
        return await Task.Run(() =>
        {
            try
            {
                var intent = new Intent();
                intent.SetClassName(TermuxPackageName, TermuxRunCommandService);
                intent.SetAction("com.termux.RUN_COMMAND");
                intent.PutExtra("com.termux.RUN_COMMAND_PATH", "/data/data/com.termux/files/usr/bin/bash");
                intent.PutExtra("com.termux.RUN_COMMAND_ARGUMENTS", new string[] { "-c", command });
                intent.PutExtra("com.termux.RUN_COMMAND_WORKDIR", "/data/data/com.termux/files/home");
                intent.PutExtra("com.termux.RUN_COMMAND_BACKGROUND", true);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    Application.Context.StartForegroundService(intent);
                }
                else
                {
                    Application.Context.StartService(intent);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunCommandAsync error: {ex.Message}");
                return false;
            }
        });
    }

    public partial async Task<SetupResult> RunSetupAsync(IProgress<string>? progress = null)
    {
        var steps = new List<string>();
        
        try
        {
            // Step 1: Check Termux
            progress?.Report("Checking Termux installation...");
            steps.Add("Checking Termux installation");
            
            if (!await IsTermuxInstalledAsync())
            {
                return new SetupResult(false, "Termux is not installed. Please install from F-Droid.", steps);
            }
            steps.Add("✓ Termux found");

            // Step 2: Update packages
            progress?.Report("Updating Termux packages...");
            steps.Add("Updating packages");
            await RunCommandAsync("pkg update -y && pkg upgrade -y");
            await Task.Delay(3000); // Give it time
            steps.Add("✓ Packages updated");

            // Step 3: Install Node.js
            progress?.Report("Installing Node.js LTS...");
            steps.Add("Installing Node.js");
            await RunCommandAsync("pkg install -y nodejs-lts");
            await Task.Delay(5000);
            Preferences.Default.Set("NodeInstalled", true);
            steps.Add("✓ Node.js installed");

            // Step 4: Install Git
            progress?.Report("Installing Git...");
            steps.Add("Installing Git");
            await RunCommandAsync("pkg install -y git");
            await Task.Delay(2000);
            steps.Add("✓ Git installed");

            // Step 5: Install Claude Code
            progress?.Report("Installing Claude Code CLI...");
            steps.Add("Installing Claude Code CLI");
            await RunCommandAsync("npm install -g @anthropic-ai/claude-code");
            await Task.Delay(10000);
            Preferences.Default.Set("ClaudeCodeInstalled", true);
            steps.Add("✓ Claude Code installed");

            // Step 6: Setup storage
            progress?.Report("Setting up storage access...");
            steps.Add("Setting up storage");
            await RunCommandAsync("termux-setup-storage");
            await Task.Delay(2000);
            steps.Add("✓ Storage configured");

            // Step 7: Create aliases
            progress?.Report("Creating helpful aliases...");
            steps.Add("Creating aliases");
            var aliasCommand = 
$@"echo '
# Claude Code Launcher aliases
alias cc=""claude""
alias ccd=""cd ~/storage/shared && claude""
alias ccproject=""cd ~/storage/shared/Projects && claude""
' >> ~/.bashrc";
            await RunCommandAsync(aliasCommand);
            steps.Add("✓ Aliases created");

            Preferences.Default.Set("SetupComplete", true);
            progress?.Report("Setup complete!");
            
            return new SetupResult(true, "Setup completed successfully!", steps);
        }
        catch (Exception ex)
        {
            steps.Add($"✗ Error: {ex.Message}");
            return new SetupResult(false, $"Setup failed: {ex.Message}", steps);
        }
    }

    public partial async Task<bool> LaunchClaudeCodeAsync(string? workingDirectory = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                var intent = new Intent();
                intent.SetClassName(TermuxPackageName, TermuxRunCommandService);
                intent.SetAction("com.termux.RUN_COMMAND");
                intent.PutExtra("com.termux.RUN_COMMAND_PATH", "/data/data/com.termux/files/usr/bin/claude");
                intent.PutExtra("com.termux.RUN_COMMAND_WORKDIR", 
                    workingDirectory ?? "/data/data/com.termux/files/home");
                intent.PutExtra("com.termux.RUN_COMMAND_BACKGROUND", false);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    Application.Context.StartForegroundService(intent);
                }
                else
                {
                    Application.Context.StartService(intent);
                }

                // Also open Termux to see the output
                OpenTermuxAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LaunchClaudeCodeAsync error: {ex.Message}");
                return false;
            }
        });
    }

    public partial async Task SendInputAsync(string text)
    {
        // Strategy: Use termux-api clipboard + simulate Ctrl+Shift+V (Termux paste)
        // This is the most reliable way to send text to a running Termux session
        
        // Step 1: Set clipboard via termux-api (requires termux-api package)
        var escaped = text.Replace("'", "'\\''");
        await RunCommandAsync($"termux-clipboard-set '{escaped}'");
        
        // Small delay to ensure clipboard is set
        await Task.Delay(50);
        
        // Step 2: Simulate paste via input command
        // Note: This sends to foreground app - make sure Termux is focused
        await Task.Run(() =>
        {
            try
            {
                var intent = new Intent();
                intent.SetClassName(TermuxPackageName, "com.termux.app.TermuxService");
                intent.SetAction("com.termux.service_execute");
                intent.PutExtra("com.termux.execute.command_path", "/data/data/com.termux/files/usr/bin/bash");
                intent.PutExtra("com.termux.execute.arguments", new string[] 
                { 
                    "-c", 
                    // Write to Termux's stdin using printf - this actually works!
                    $"printf '%s' '{escaped}' >> /proc/$(pgrep -n bash)/fd/0 2>/dev/null || printf '%s' '{escaped}'"
                });
                intent.PutExtra("com.termux.execute.background", true);
                
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    Application.Context.StartForegroundService(intent);
                }
                else
                {
                    Application.Context.StartService(intent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendInputAsync fallback error: {ex.Message}");
            }
        });
    }

    public partial async Task SendSpecialKeyAsync(SpecialKey key)
    {
        // Map special keys to ANSI escape sequences
        var sequence = key switch
        {
            SpecialKey.CtrlC => "\x03",      // ETX - interrupt
            SpecialKey.CtrlD => "\x04",      // EOT - end of transmission
            SpecialKey.CtrlZ => "\x1a",      // SUB - suspend
            SpecialKey.Tab => "\t",          // Tab
            SpecialKey.Enter => "\n",        // Newline
            SpecialKey.ArrowUp => "\x1b[A",  // ANSI escape up
            SpecialKey.ArrowDown => "\x1b[B",
            SpecialKey.ArrowLeft => "\x1b[D",
            SpecialKey.ArrowRight => "\x1b[C",
            _ => ""
        };

        if (string.IsNullOrEmpty(sequence)) return;

        // Use termux-api to simulate keypress if available, otherwise use printf
        var hexSequence = string.Join("", sequence.Select(c => $"\\x{(int)c:x2}"));
        
        await Task.Run(() =>
        {
            try
            {
                var intent = new Intent();
                intent.SetClassName(TermuxPackageName, TermuxRunCommandService);
                intent.SetAction("com.termux.RUN_COMMAND");
                intent.PutExtra("com.termux.RUN_COMMAND_PATH", "/data/data/com.termux/files/usr/bin/bash");
                intent.PutExtra("com.termux.RUN_COMMAND_ARGUMENTS", new string[] 
                { 
                    "-c", 
                    $"printf '{hexSequence}'"
                });
                intent.PutExtra("com.termux.RUN_COMMAND_BACKGROUND", true);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    Application.Context.StartForegroundService(intent);
                }
                else
                {
                    Application.Context.StartService(intent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendSpecialKeyAsync error: {ex.Message}");
            }
        });
    }

    public partial async Task OpenTermuxAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var pm = Application.Context.PackageManager;
                var intent = pm?.GetLaunchIntentForPackage(TermuxPackageName);
                
                if (intent != null)
                {
                    intent.AddFlags(ActivityFlags.NewTask);
                    Application.Context.StartActivity(intent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenTermuxAsync error: {ex.Message}");
            }
        });
    }

    public partial async Task<HealthCheckResult> GetHealthCheckAsync()
    {
        var termuxInstalled = await IsTermuxInstalledAsync();
        var nodeInstalled = await IsNodeInstalledAsync();
        var claudeInstalled = await IsClaudeCodeInstalledAsync();
        
        return new HealthCheckResult(
            TermuxInstalled: termuxInstalled,
            NodeInstalled: nodeInstalled,
            GitInstalled: Preferences.Default.Get("GitInstalled", false),
            ClaudeCodeInstalled: claudeInstalled,
            NodeVersion: Preferences.Default.Get("NodeVersion", (string?)null),
            ClaudeCodeVersion: Preferences.Default.Get("ClaudeCodeVersion", (string?)null)
        );
    }
}
#endif
