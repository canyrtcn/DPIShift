using System.Windows.Media;
using DPIShift.Core.Localization;
using DPIShift.Core.Models;
using DPIShift.Core.Native;

namespace DPIShift.UI.ViewModels;

public sealed class AppItemViewModel : ObservableObject
{
    private ImageSource? _icon;
    private bool _iconLoaded;
    private int? _configuredScalePercent;
    private bool _isRunning;

    public DiscoveredApp Model { get; }

    public AppItemViewModel(DiscoveredApp model, AppScaleProfile? profile)
    {
        Model = model;
        _isRunning = model.IsRunning;
        _configuredScalePercent = profile is { IsEnabled: true } && profile.ScalePercent != 100
            ? profile.ScalePercent
            : null;
    }

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string ExecutablePath => Model.ExecutablePath;
    public string ExecutableFileName => Path.GetFileName(Model.ExecutablePath);
    public string PublisherOrFileName => string.IsNullOrWhiteSpace(Model.Publisher) ? ExecutableFileName : $"{Model.Publisher} · {ExecutableFileName}";

    public CompatibilityReport Compatibility => Model.Compatibility;
    public SupportLevel Support => Compatibility.Support;
    public string SupportBadgeText => Compatibility.SupportBadgeText;
    public string TechnologyBadge => Compatibility.TechnologyBadge;
    public string DpiAwarenessLabel => Compatibility.DpiAwarenessLabel;
    public bool CanScale => Compatibility.CanScale;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = IconExtractor.GetIconForExecutable(Model.ExecutablePath);
            }
            return _icon;
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                Model.IsRunning = value;
                OnPropertyChanged(nameof(RunningStateText));
            }
        }
    }

    public string RunningStateText => IsRunning
        ? Loc.Pick("Running", "Çalışıyor")
        : Loc.Pick("Stopped", "Kapalı");

    public int? ConfiguredScalePercent
    {
        get => _configuredScalePercent;
        set
        {
            if (SetProperty(ref _configuredScalePercent, value))
            {
                OnPropertyChanged(nameof(HasCustomScale));
                OnPropertyChanged(nameof(ConfiguredScaleBadge));
            }
        }
    }

    public bool HasCustomScale => _configuredScalePercent is not null && _configuredScalePercent != 100;

    public string ConfiguredScaleBadge => HasCustomScale
        ? (Loc.IsTurkish ? $"%{_configuredScalePercent}" : $"{_configuredScalePercent}%")
        : string.Empty;

    public void SyncFromModelAndProfile(AppScaleProfile? profile)
    {
        IsRunning = Model.IsRunning;
        ConfiguredScalePercent = profile is { IsEnabled: true } && profile.ScalePercent != 100
            ? profile.ScalePercent
            : null;
        NotifyLocalizationChanged();
    }

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(RunningStateText));
        OnPropertyChanged(nameof(ConfiguredScaleBadge));
        OnPropertyChanged(nameof(Compatibility));
        OnPropertyChanged(nameof(Support));
        OnPropertyChanged(nameof(SupportBadgeText));
        OnPropertyChanged(nameof(TechnologyBadge));
        OnPropertyChanged(nameof(DpiAwarenessLabel));
        OnPropertyChanged(nameof(CanScale));
    }
}
