namespace ClaudeCodeLauncher.Services;

/// <summary>
/// Cross-platform shell of TermuxService.
/// Android-specific implementation is in Platforms/Android/Services/TermuxService.Android.cs
/// </summary>
public partial class TermuxService : ITermuxService
{
    public partial Task<bool> IsTermuxInstalledAsync();
    public partial Task<bool> IsClaudeCodeInstalledAsync();
    public partial Task<bool> IsNodeInstalledAsync();
    public partial Task<bool> RunCommandAsync(string command);
    public partial Task<SetupResult> RunSetupAsync(IProgress<string>? progress = null);
    public partial Task<bool> LaunchClaudeCodeAsync(string? workingDirectory = null);
    public partial Task SendInputAsync(string text);
    public partial Task SendSpecialKeyAsync(SpecialKey key);
    public partial Task OpenTermuxAsync();
    public partial Task<HealthCheckResult> GetHealthCheckAsync();
}
