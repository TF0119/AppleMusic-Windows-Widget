using System.Diagnostics;
using Microsoft.Win32;

namespace AppleMusicWidget.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AppleMusicWidget";

    public static bool TrySetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                {
                    Debug.WriteLine("[Startup] ProcessPath is empty");
                    return false;
                }
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] TrySetEnabled({enabled}) failed: {ex.Message}");
            return false;
        }
    }
}
