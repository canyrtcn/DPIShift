using DPIShift.Core.Models;
using DPIShift.Core.Native;
using Microsoft.Win32;

namespace DPIShift.Core.Scaling;

public sealed class ShortcutIntegrationService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly IReadOnlyList<string>? _customSearchFolders;
    private readonly bool _enableRegistryRunIntegration;

    public ShortcutIntegrationService(
        IReadOnlyList<string>? customSearchFolders = null,
        bool enableRegistryRunIntegration = true)
    {
        _customSearchFolders = customSearchFolders;
        _enableRegistryRunIntegration = enableRegistryRunIntegration;
    }

    public int ApplyUserShortcutsAndStartup(AppScaleProfile profile)
    {
        if (!profile.UpdateUserShortcuts)
            return 0;

        if (profile.DeliveryMode is not (ScalingDeliveryMode.ChromiumFlags or ScalingDeliveryMode.EclipseIniOrArgs))
            return 0;

        int updatedCount = 0;

        foreach ((string folder, bool recurse) in GetUserWritableShortcutFolders())
        {
            if (!Directory.Exists(folder))
                continue;

            IEnumerable<string> lnkFiles;
            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = recurse,
                    MaxRecursionDepth = 5,
                    ReturnSpecialDirectories = false
                };
                lnkFiles = Directory.EnumerateFiles(folder, "*.lnk", enumOptions);
            }
            catch
            {
                continue;
            }

            foreach (string lnkPath in lnkFiles)
            {
                ShellLinkInfo? info = ShellLinkInterop.TryReadShortcut(lnkPath);
                if (info is null || string.IsNullOrWhiteSpace(info.TargetPath))
                    continue;

                // Never double-patch DPIShift's own generated --launch shortcuts
                if (info.Arguments.Contains("--launch", StringComparison.OrdinalIgnoreCase))
                    continue;

                ShortcutMatchKind match = MatchShortcutToProfile(info, profile);
                if (match == ShortcutMatchKind.None)
                    continue;

                // Record original untouched backup BEFORE first modification only (idempotent across scale edits!)
                if (!profile.ModifiedShortcuts.Any(b => string.Equals(b.ShortcutPath, lnkPath, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.ModifiedShortcuts.Add(new ShortcutBackupEntry
                    {
                        ShortcutPath = lnkPath,
                        OriginalTargetPath = info.TargetPath,
                        OriginalArguments = info.Arguments,
                        OriginalWorkingDirectory = info.WorkingDirectory,
                        OriginalIconLocation = info.IconLocation,
                        OriginalIconIndex = info.IconIndex
                    });
                }

                // Always compute updated arguments from the backed-up original arguments to stay 100% clean
                ShortcutBackupEntry backup = profile.ModifiedShortcuts.First(b =>
                    string.Equals(b.ShortcutPath, lnkPath, StringComparison.OrdinalIgnoreCase));

                string newArgs = BuildPatchedArguments(backup.OriginalArguments, profile, match);
                var updatedLink = new ShellLinkInfo
                {
                    ShortcutPath = lnkPath,
                    TargetPath = backup.OriginalTargetPath,
                    Arguments = newArgs,
                    WorkingDirectory = backup.OriginalWorkingDirectory,
                    Description = info.Description,
                    IconLocation = backup.OriginalIconLocation,
                    IconIndex = backup.OriginalIconIndex
                };

                if (ShellLinkInterop.TryWriteShortcut(updatedLink))
                    updatedCount++;
            }
        }

        if (_enableRegistryRunIntegration &&
            profile.DetectedTech == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath) &&
            !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
        {
            updatedCount += ApplySquirrelRegistryRunEntry(profile);
        }

        return updatedCount;
    }

    public void RevertShortcutsAndStartup(AppScaleProfile profile)
    {
        foreach (ShortcutBackupEntry backup in profile.ModifiedShortcuts.ToList())
        {
            if (File.Exists(backup.ShortcutPath))
            {
                var restoreInfo = new ShellLinkInfo
                {
                    ShortcutPath = backup.ShortcutPath,
                    TargetPath = backup.OriginalTargetPath,
                    Arguments = backup.OriginalArguments,
                    WorkingDirectory = backup.OriginalWorkingDirectory,
                    IconLocation = backup.OriginalIconLocation,
                    IconIndex = backup.OriginalIconIndex
                };
                ShellLinkInterop.TryWriteShortcut(restoreInfo);
            }
        }
        profile.ModifiedShortcuts.Clear();

        if (_enableRegistryRunIntegration && profile.ModifiedRunEntries.Count > 0)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
                if (key is not null)
                {
                    foreach (RegistryRunBackupEntry entry in profile.ModifiedRunEntries)
                    {
                        if (key.GetValue(entry.ValueName) is not null)
                            key.SetValue(entry.ValueName, entry.OriginalCommand, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Ignore
            }
            profile.ModifiedRunEntries.Clear();
        }

        if (!string.IsNullOrWhiteSpace(profile.CreatedDesktopLauncherPath) &&
            File.Exists(profile.CreatedDesktopLauncherPath))
        {
            try { File.Delete(profile.CreatedDesktopLauncherPath); } catch { }
        }
        profile.CreatedDesktopLauncherPath = null;
    }

    public string? CreateScaledDesktopShortcut(AppScaleProfile profile, string? targetDesktopFolder = null)
    {
        string desktop = targetDesktopFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            return null;

        string safeName = string.Concat(profile.DisplayName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);

        string lnkPath = Path.Combine(desktop, $"{safeName} (DPIShift).lnk");

        ShellLinkInfo linkInfo;
        if (profile.DeliveryMode == ScalingDeliveryMode.ChromiumFlags)
        {
            if (profile.DetectedTech == ScalingTech.ElectronSquirrel &&
                !string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath) &&
                !string.IsNullOrWhiteSpace(profile.SquirrelProcessName) &&
                File.Exists(profile.SquirrelUpdateExePath))
            {
                linkInfo = new ShellLinkInfo
                {
                    ShortcutPath = lnkPath,
                    TargetPath = profile.SquirrelUpdateExePath,
                    Arguments = ScaleMath.InjectSquirrelProcessStartArgs(null, profile.SquirrelProcessName, profile.ScalePercent),
                    WorkingDirectory = Path.GetDirectoryName(profile.SquirrelUpdateExePath) ?? string.Empty,
                    Description = $"{profile.DisplayName} (%{profile.ScalePercent} DPIShift)",
                    IconLocation = File.Exists(profile.ExecutablePath) ? profile.ExecutablePath : profile.SquirrelUpdateExePath,
                    IconIndex = 0
                };
            }
            else
            {
                linkInfo = new ShellLinkInfo
                {
                    ShortcutPath = lnkPath,
                    TargetPath = profile.ExecutablePath,
                    Arguments = ScaleMath.BuildChromiumFlags(profile.ScalePercent),
                    WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? string.Empty,
                    Description = $"{profile.DisplayName} (%{profile.ScalePercent} DPIShift)",
                    IconLocation = profile.ExecutablePath,
                    IconIndex = 0
                };
            }
        }
        else if (profile.DeliveryMode == ScalingDeliveryMode.EclipseIniOrArgs)
        {
            linkInfo = new ShellLinkInfo
            {
                ShortcutPath = lnkPath,
                TargetPath = profile.ExecutablePath,
                Arguments = ScaleMath.BuildEclipseAppendVmArgs(profile.ScalePercent),
                WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? string.Empty,
                Description = $"{profile.DisplayName} (%{profile.ScalePercent} DPIShift)",
                IconLocation = profile.ExecutablePath,
                IconIndex = 0
            };
        }
        else
        {
            // For Qt, GTK, WebView2, Java, or LiveRuntime apps: invoke DPIShift fast-launcher with the app's native icon!
            string? selfExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(selfExe))
                return null;

            linkInfo = new ShellLinkInfo
            {
                ShortcutPath = lnkPath,
                TargetPath = selfExe,
                Arguments = $"--launch \"{profile.Id}\"",
                WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? string.Empty,
                Description = $"{profile.DisplayName} (%{profile.ScalePercent} DPIShift)",
                IconLocation = profile.ExecutablePath,
                IconIndex = 0
            };
        }

        if (ShellLinkInterop.TryWriteShortcut(linkInfo))
        {
            profile.CreatedDesktopLauncherPath = lnkPath;
            return lnkPath;
        }

        return null;
    }

    private int ApplySquirrelRegistryRunEntry(AppScaleProfile profile)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
                return 0;

            int count = 0;
            foreach (string valueName in key.GetValueNames())
            {
                if (string.Equals(valueName, "DPIShift", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (key.GetValue(valueName) is not string cmd || string.IsNullOrWhiteSpace(cmd))
                    continue;

                if (!cmd.Contains(profile.SquirrelUpdateExePath!, StringComparison.OrdinalIgnoreCase) &&
                    !cmd.Contains(profile.SquirrelProcessName!, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!cmd.Contains("Update.exe", StringComparison.OrdinalIgnoreCase) ||
                    !cmd.Contains(profile.SquirrelProcessName!, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!profile.ModifiedRunEntries.Any(r => string.Equals(r.ValueName, valueName, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.ModifiedRunEntries.Add(new RegistryRunBackupEntry
                    {
                        ValueName = valueName,
                        OriginalCommand = cmd
                    });
                }

                RegistryRunBackupEntry backup = profile.ModifiedRunEntries.First(r =>
                    string.Equals(r.ValueName, valueName, StringComparison.OrdinalIgnoreCase));

                string stripped = ScaleMath.StripSquirrelChromiumFlags(backup.OriginalCommand);
                string flags = ScaleMath.BuildChromiumFlags(profile.ScalePercent);
                string updatedCmd = $"{stripped} --process-start-args \"{flags}\"";

                key.SetValue(valueName, updatedCmd, RegistryValueKind.String);
                count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private enum ShortcutMatchKind
    {
        None = 0,
        DirectExecutable,
        SquirrelUpdater
    }

    private static ShortcutMatchKind MatchShortcutToProfile(ShellLinkInfo info, AppScaleProfile profile)
    {
        string target;
        try
        {
            target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(info.TargetPath.Trim('"')));
        }
        catch
        {
            return ShortcutMatchKind.None;
        }

        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            try
            {
                string profileExe = Path.GetFullPath(profile.ExecutablePath);
                if (string.Equals(target, profileExe, StringComparison.OrdinalIgnoreCase))
                    return ShortcutMatchKind.DirectExecutable;
            }
            catch { }
        }

        if (profile.DetectedTech == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath) &&
            !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
        {
            try
            {
                string updateExe = Path.GetFullPath(profile.SquirrelUpdateExePath);
                if (string.Equals(target, updateExe, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(info.Arguments) ||
                     info.Arguments.Contains(profile.SquirrelProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    return ShortcutMatchKind.SquirrelUpdater;
                }

                string? squirrelRoot = Path.GetDirectoryName(updateExe);
                if (!string.IsNullOrEmpty(squirrelRoot) &&
                    target.StartsWith(squirrelRoot, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileName(target), profile.SquirrelProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return ShortcutMatchKind.DirectExecutable;
                }
            }
            catch { }
        }

        return ShortcutMatchKind.None;
    }

    private static string BuildPatchedArguments(string originalArgs, AppScaleProfile profile, ShortcutMatchKind match)
    {
        if (profile.DeliveryMode == ScalingDeliveryMode.ChromiumFlags)
        {
            if (match == ShortcutMatchKind.SquirrelUpdater && !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
            {
                return ScaleMath.InjectSquirrelProcessStartArgs(originalArgs, profile.SquirrelProcessName, profile.ScalePercent);
            }

            return ScaleMath.InjectChromiumFlags(originalArgs, profile.ScalePercent);
        }

        if (profile.DeliveryMode == ScalingDeliveryMode.EclipseIniOrArgs)
        {
            string eclipseArgs = ScaleMath.BuildEclipseAppendVmArgs(profile.ScalePercent);
            return string.IsNullOrWhiteSpace(originalArgs) ? eclipseArgs : $"{originalArgs.Trim()} {eclipseArgs}";
        }

        return originalArgs;
    }

    private IEnumerable<(string Path, bool Recurse)> GetUserWritableShortcutFolders()
    {
        if (_customSearchFolders is not null)
            return _customSearchFolders.Select(f => (f, true));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string taskbarPinned = Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        (string Path, bool Recurse)[] folders =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), false),
            (taskbarPinned, false)
        ];

        return folders
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase);
    }
}
