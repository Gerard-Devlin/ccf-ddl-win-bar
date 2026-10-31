using CcfDeadlineIsland.Services;
using Microsoft.Win32;

namespace CcfDeadlineIsland;

// Uses only a unique test subkey. It never reads or writes the Windows Run key.
internal static class StartupRegistrationChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException("Startup registration check: " + message);
            checks++;
        }
        bool Rejects(string path)
        {
            try { StartupRegistration.CommandForExecutable(path); return false; }
            catch (ArgumentException) { return true; }
        }

        const string executable = @"C:\Users\测试 User\Programs\CCF Deadline Island.exe";
        const string expected = "\"" + executable + "\"";
        Check(StartupRegistration.CommandForExecutable(executable) == expected,
            "spaces and Unicode are preserved inside one quoted executable command");
        Check(Rejects(@"C:\Program Files\dotnet\dotnet.exe"), "the dotnet runtime host cannot be registered");
        Check(StartupRegistration.CommandForExecutable(@"C:\" + new string('a', 251) + ".exe").Length == 260,
            "the Windows Run command-length boundary is accepted");
        Check(Rejects(@"C:\" + new string('a', 252) + ".exe"), "commands exceeding 260 characters are rejected");

        // The suffix is generated here, never supplied by callers. Cleanup must
        // target only this exact GUID key, preserving the shared parent and siblings.
        string testKeyPath = @"Software\CCFDeadlineIsland\Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(testKeyPath, writable: true)
                ?? throw new InvalidOperationException("Cannot create isolated startup test key."))
            {
                key.SetValue("UnrelatedSibling", "preserve-me", RegistryValueKind.String);
                Check(StartupRegistration.SetEnabledInKey(key, true, executable)
                    && StartupRegistration.IsEnabledInKey(key, executable), "enable registers this application in the isolated key");

                using (var reopened = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)
                    ?? throw new InvalidOperationException("Cannot reopen isolated startup test key."))
                {
                    Check(StartupRegistration.IsEnabledInKey(reopened, executable)
                        && reopened.GetValue(StartupRegistration.ValueName) is string command && command == expected,
                        "startup state persists when the registry key is reopened");
                    Check(!StartupRegistration.SetEnabledInKey(reopened, true, @"C:\Other Application.exe")
                        && reopened.GetValue(StartupRegistration.ValueName) is string unchanged && unchanged == expected,
                        "a read-only registry failure is nonfatal and preserves the existing command");
                }

                Check(!StartupRegistration.SetEnabledInKey(key, true, "relative.exe")
                    && key.GetValue(StartupRegistration.ValueName) is string original && original == expected,
                    "malformed paths do not overwrite a working registration");
                Check(StartupRegistration.SetEnabledInKey(key, false)
                    && key.GetValue(StartupRegistration.ValueName) is null
                    && !StartupRegistration.IsEnabledInKey(key, executable), "disable removes only the application's startup value");
                Check(key.GetValue("UnrelatedSibling") is string sibling && sibling == "preserve-me",
                    "disabling startup preserves unrelated sibling values");
                Check(StartupRegistration.SetEnabledInKey(key, false), "disabling an already missing value is harmless");
            }
            return checks;
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false); }
    }
}
