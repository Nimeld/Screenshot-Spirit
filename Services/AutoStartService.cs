using Microsoft.Win32;

namespace ScreenshotSpirit.Services;

public static class AutoStartService
{
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ScreenshotSpirit";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, true);
            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key?.SetValue(AppName, $"\"{exePath}\" --silent");
            }
            else
            {
                key?.DeleteValue(AppName, false);
            }
        }
        catch { }
    }
}
