namespace DPIShift.Core.Models;

public sealed class AppScaleProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public int ScalePercent { get; set; } = 125;
    public bool IsEnabled { get; set; } = true;
    public bool UpdateUserShortcuts { get; set; } = true;
    public bool AutoApplyOnWindowOpen { get; set; } = true;
    public ScalingTech DetectedTech { get; set; } = ScalingTech.Unknown;
    public ScalingDeliveryMode DeliveryMode { get; set; } = ScalingDeliveryMode.None;
    public string? SquirrelUpdateExePath { get; set; }
    public string? SquirrelProcessName { get; set; }
    public string? JetBrainsVmOptionsPath { get; set; }
    public string? EclipseIniPath { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ShortcutBackupEntry> ModifiedShortcuts { get; set; } = [];
    public List<RegistryRunBackupEntry> ModifiedRunEntries { get; set; } = [];
    public string? ModifiedConfigFilePath { get; set; }
    public bool CreatedConfigFileFromScratch { get; set; }
    public string? CreatedDesktopLauncherPath { get; set; }
}

public sealed class ShortcutBackupEntry
{
    public string ShortcutPath { get; set; } = string.Empty;
    public string OriginalTargetPath { get; set; } = string.Empty;
    public string OriginalArguments { get; set; } = string.Empty;
    public string OriginalWorkingDirectory { get; set; } = string.Empty;
    public string OriginalIconLocation { get; set; } = string.Empty;
    public int OriginalIconIndex { get; set; }
}

public sealed class RegistryRunBackupEntry
{
    public string ValueName { get; set; } = string.Empty;
    public string OriginalCommand { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedInTray { get; set; } = true;
    public bool EnableBackgroundRuntimeScaling { get; set; } = true;
    public bool ShowTrayNotificationOnFirstClose { get; set; } = true;
}

public sealed class DpiShiftConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<AppScaleProfile> Profiles { get; set; } = [];
}
