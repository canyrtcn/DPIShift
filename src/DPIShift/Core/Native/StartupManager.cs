using Microsoft.Win32;

namespace DPIShift.Core.Native;

public static class StartupManager
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "DPIShift";

    public static bool IsRegisteredForStartup()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            string? val = key?.GetValue(AppValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartupRegistration(bool enable, bool startMinimized = true)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
                return false;

            if (!enable)
            {
                if (key.GetValue(AppValueName) is not null)
                    key.DeleteValue(AppValueName, throwOnMissingValue: false);
                return true;
            }

            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                return false;

            string command = startMinimized
                ? $"\"{processPath}\" --minimized"
                : $"\"{processPath}\"";

            key.SetValue(AppValueName, command, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
