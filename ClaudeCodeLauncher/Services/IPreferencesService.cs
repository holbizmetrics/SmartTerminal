namespace ClaudeCodeLauncher.Services;

/// <summary>
/// Service for storing app preferences
/// </summary>
public interface IPreferencesService
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    bool ContainsKey(string key);
    void Remove(string key);
    void Clear();
}
