using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace AdventureBackpacks.Configuration;

// Member names are the values users write into "Log Output Configuration / Log Level".
public enum LogLevels
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

// BepInEx log source whose output is gated by the "Log Output Configuration" entries.
// Message always passes; before the entries are bound, everything passes.
public sealed class ModLogger
{
    private readonly ManualLogSource _source;
    private ConfigEntry<bool> _enabled;
    private ConfigEntry<LogLevels> _minimum;

    public ModLogger(string sourceName)
    {
        _source = Logger.CreateLogSource(sourceName);
    }

    internal void UseFilter(ConfigEntry<bool> enabled, ConfigEntry<LogLevels> minimum)
    {
        _enabled = enabled;
        _minimum = minimum;
    }

    public void Debug(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        Write(LogLevels.Debug, LogLevel.Debug, message, file, member);
    }

    public void Info(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        Write(LogLevels.Info, LogLevel.Info, message, file, member);
    }

    public void Warning(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        Write(LogLevels.Warning, LogLevel.Warning, message, file, member);
    }

    public void LogError(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        Write(LogLevels.Error, LogLevel.Error, message, file, member);
    }

    public void Fatal(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        Write(LogLevels.Fatal, LogLevel.Fatal, message, file, member);
    }

    public void Message(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        _source.Log(LogLevel.Message, Format(message, file, member));
    }

    private void Write(LogLevels threshold, LogLevel level, string message, string file, string member)
    {
        if (_enabled != null && _minimum != null && (!_enabled.Value || threshold < _minimum.Value))
            return;

        _source.Log(level, Format(message, file, member));
    }

    private static string Format(string message, string file, string member)
    {
        return $"[{Path.GetFileNameWithoutExtension(file)}.{member}] {message}";
    }
}
