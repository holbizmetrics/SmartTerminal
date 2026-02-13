namespace ClaudeCodeLauncher.Services;

/// <summary>
/// Preferences service using MAUI Preferences API
/// </summary>
public class PreferencesService : IPreferencesService
{
    public T Get<T>(string key, T defaultValue)
    {
        if (typeof(T) == typeof(bool))
            return (T)(object)Preferences.Default.Get(key, (bool)(object)defaultValue!);
        if (typeof(T) == typeof(int))
            return (T)(object)Preferences.Default.Get(key, (int)(object)defaultValue!);
        if (typeof(T) == typeof(string))
            return (T)(object)Preferences.Default.Get(key, (string?)(object?)defaultValue ?? string.Empty)!;
        if (typeof(T) == typeof(double))
            return (T)(object)Preferences.Default.Get(key, (double)(object)defaultValue!);
        if (typeof(T) == typeof(long))
            return (T)(object)Preferences.Default.Get(key, (long)(object)defaultValue!);
        if (typeof(T) == typeof(float))
            return (T)(object)Preferences.Default.Get(key, (float)(object)defaultValue!);
        if (typeof(T) == typeof(DateTime))
            return (T)(object)Preferences.Default.Get(key, (DateTime)(object)defaultValue!);
            
        throw new NotSupportedException($"Type {typeof(T)} is not supported by Preferences");
    }

    public void Set<T>(string key, T value)
    {
        if (value is bool b)
            Preferences.Default.Set(key, b);
        else if (value is int i)
            Preferences.Default.Set(key, i);
        else if (value is string s)
            Preferences.Default.Set(key, s);
        else if (value is double d)
            Preferences.Default.Set(key, d);
        else if (value is long l)
            Preferences.Default.Set(key, l);
        else if (value is float f)
            Preferences.Default.Set(key, f);
        else if (value is DateTime dt)
            Preferences.Default.Set(key, dt);
        else
            throw new NotSupportedException($"Type {typeof(T)} is not supported by Preferences");
    }

    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    
    public void Remove(string key) => Preferences.Default.Remove(key);
    
    public void Clear() => Preferences.Default.Clear();
}
