namespace ClaudeCodeLauncher.Services;

/// <summary>
/// Service for interacting with Termux app
/// </summary>
public interface ITermuxService
{
    /// <summary>
    /// Check if Termux is installed
    /// </summary>
    Task<bool> IsTermuxInstalledAsync();
    
    /// <summary>
    /// Check if Claude Code CLI is installed in Termux
    /// </summary>
    Task<bool> IsClaudeCodeInstalledAsync();
    
    /// <summary>
    /// Check if Node.js is installed in Termux
    /// </summary>
    Task<bool> IsNodeInstalledAsync();
    
    /// <summary>
    /// Run a command in Termux (background)
    /// </summary>
    Task<bool> RunCommandAsync(string command);
    
    /// <summary>
    /// Run setup script to install dependencies
    /// </summary>
    Task<SetupResult> RunSetupAsync(IProgress<string>? progress = null);
    
    /// <summary>
    /// Launch Claude Code in Termux
    /// </summary>
    Task<bool> LaunchClaudeCodeAsync(string? workingDirectory = null);
    
    /// <summary>
    /// Send text input to Termux
    /// </summary>
    Task SendInputAsync(string text);
    
    /// <summary>
    /// Send special key to Termux (Ctrl+C, etc.)
    /// </summary>
    Task SendSpecialKeyAsync(SpecialKey key);
    
    /// <summary>
    /// Open Termux app
    /// </summary>
    Task OpenTermuxAsync();
    
    /// <summary>
    /// Get health check status
    /// </summary>
    Task<HealthCheckResult> GetHealthCheckAsync();
}

public enum SpecialKey
{
    CtrlC,
    CtrlD,
    CtrlZ,
    Tab,
    Enter,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight
}

public record SetupResult(bool Success, string Message, List<string> Steps);

public record HealthCheckResult(
    bool TermuxInstalled,
    bool NodeInstalled,
    bool GitInstalled,
    bool ClaudeCodeInstalled,
    string? NodeVersion,
    string? ClaudeCodeVersion
);
