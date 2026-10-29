using DPIShift.Core.Localization;

namespace DPIShift.Core.Models;

public sealed class CompatibilityReport
{
    private readonly string _technologyBadge = "Unknown";
    private readonly string _dpiAwarenessLabel = "Unknown";
    private readonly string _summaryTitle = string.Empty;
    private readonly string _explanation = string.Empty;
    private readonly string _mechanismDescription = string.Empty;

    public ScalingTech Technology { get; init; } = ScalingTech.Unknown;

    public string TechnologyBadge
    {
        get => Loc.IsTurkish && !string.IsNullOrEmpty(TechnologyBadgeTr) ? TechnologyBadgeTr : _technologyBadge;
        init => _technologyBadge = value;
    }

    public string? TechnologyBadgeTr { get; init; }

    public SupportLevel Support { get; init; } = SupportLevel.Unsupported;
    public ScalingDeliveryMode DeliveryMode { get; init; } = ScalingDeliveryMode.None;

    public string DpiAwarenessLabel
    {
        get => Loc.IsTurkish && !string.IsNullOrEmpty(DpiAwarenessLabelTr) ? DpiAwarenessLabelTr : _dpiAwarenessLabel;
        init => _dpiAwarenessLabel = value;
    }

    public string? DpiAwarenessLabelTr { get; init; }

    public string SummaryTitle
    {
        get => Loc.IsTurkish && !string.IsNullOrEmpty(SummaryTitleTr) ? SummaryTitleTr : _summaryTitle;
        init => _summaryTitle = value;
    }

    public string? SummaryTitleTr { get; init; }

    public string Explanation
    {
        get => Loc.IsTurkish && !string.IsNullOrEmpty(ExplanationTr) ? ExplanationTr : _explanation;
        init => _explanation = value;
    }

    public string? ExplanationTr { get; init; }

    public string MechanismDescription
    {
        get => Loc.IsTurkish && !string.IsNullOrEmpty(MechanismDescriptionTr) ? MechanismDescriptionTr : _mechanismDescription;
        init => _mechanismDescription = value;
    }

    public string? MechanismDescriptionTr { get; init; }

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
        SupportLevel.Full => Loc.Pick("Full Support", "Tam Destek"),
        SupportLevel.Partial => Loc.Pick("Partial Support", "Kısmi Destek"),
        _ => Loc.Pick("Unsupported", "Desteklenmiyor")
    };
}
