namespace DPIShift.Core.Models;

public sealed class CompatibilityReport
{
    public ScalingTech Technology { get; init; } = ScalingTech.Unknown;
    public string TechnologyBadge { get; init; } = "Bilinmiyor";
    public SupportLevel Support { get; init; } = SupportLevel.Unsupported;
    public ScalingDeliveryMode DeliveryMode { get; init; } = ScalingDeliveryMode.None;
    public string DpiAwarenessLabel { get; init; } = "Bilinmiyor";
    public string SummaryTitle { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string MechanismDescription { get; init; } = string.Empty;
    public bool RequiresRestart { get; init; } = true;
    public bool SupportsDirectShortcutFlags { get; init; }
    public bool SupportsPersistentConfigFile { get; init; }
    public string? SquirrelUpdateExePath { get; init; }
    public string? SquirrelProcessName { get; init; }
    public string? SquirrelRootDir { get; init; }
    public string? JetBrainsVmOptionsPath { get; init; }
    public string? JetBrainsProductName { get; init; }
    public string? EclipseIniPath { get; init; }

    public bool CanScale => Support is SupportLevel.Full or SupportLevel.Partial && DeliveryMode != ScalingDeliveryMode.None;

    public string SupportBadgeText => Support switch
    {
        SupportLevel.Full => "Tam Destek",
        SupportLevel.Partial => "Kısmi Destek",
        _ => "Desteklenmiyor"
    };
}
