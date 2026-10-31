using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CcfDeadlineIsland.Services;

/// <summary>Owns only this application's startup value for the current Windows user.</summary>
public static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CCFDeadlineIsland";

    public static bool IsEnabled(string? executablePath = null)
    {
        if (!TryGetCommand(executablePath ?? Environment.ProcessPath, out string command)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key is not null && HasCommand(key, command);
        }
        catch (Exception error) when (IsStorageError(error)) { return false; }
    }

    public static bool SetEnabled(bool enabled, string? executablePath = null)
    {
        // A DLL launched by dotnet/testhost must never register its host as this app.
        string? command = null;
        if (enabled && !TryGetCommand(executablePath ?? Environment.ProcessPath, out command)) return false;
        try
        {
            using var key = enabled
                ? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                : Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return !enabled;
            return WriteCommand(key, enabled, command);
        }
        catch (Exception error) when (IsStorageError(error)) { return false; }
    }

    /// <summary>Builds one quoted executable command, with no arguments or shell expansion.</summary>
    public static string CommandForExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || path.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("Startup requires an absolute executable path.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        string name = Path.GetFileName(fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "testhost.exe", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("vstest.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Startup requires the application executable, not a runtime host.", nameof(path));

        string command = "\"" + fullPath + "\"";
        // Windows Run values accept a command line of at most 260 characters.
        if (command.Length > 260)
            throw new ArgumentException("The startup command must not exceed 260 characters.", nameof(path));
        return command;
    }

    // Test with a caller-owned temporary key; these methods never select the real Run key.
    internal static bool IsEnabledInKey(RegistryKey key, string executablePath)
    {
        if (!TryGetCommand(executablePath, out string command)) return false;
        try { return HasCommand(key, command); }
        catch (Exception error) when (IsStorageError(error)) { return false; }
    }

    internal static bool SetEnabledInKey(RegistryKey key, bool enabled, string? executablePath = null)
    {
        string? command = null;
        if (enabled && !TryGetCommand(executablePath, out command)) return false;
        try { return WriteCommand(key, enabled, command); }
        catch (Exception error) when (IsStorageError(error)) { return false; }
    }

    private static bool HasCommand(RegistryKey key, string command) => key.GetValue(ValueName) is string value
        && string.Equals(value.Trim(), command, StringComparison.OrdinalIgnoreCase);

    private static bool WriteCommand(RegistryKey key, bool enabled, string? command)
    {
        if (enabled)
        {
            key.SetValue(ValueName, command!, RegistryValueKind.String);
            return HasCommand(key, command!);
        }
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return key.GetValue(ValueName) is null;
    }

    private static bool TryGetCommand(string? path, out string command)
    {
        command = "";
        if (path is null) return false;
        try { command = CommandForExecutable(path); return true; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    private static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException
        or SecurityException or ObjectDisposedException;
}
