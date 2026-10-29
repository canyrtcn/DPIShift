using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using DPIShift.Core.Detection;
using DPIShift.Core.Localization;
using DPIShift.Core.Models;
using DPIShift.Core.Native;
using DPIShift.Core.Scaling;
using Microsoft.Win32;

namespace DPIShift.UI.ViewModels;

public enum AppListFilterTab
{
    All = 0,
    Running = 1,
    Configured = 2
}

public sealed class MainViewModel : ObservableObject
{
    private readonly CompatibilityAnalyzer _analyzer;
    private readonly AppDiscoveryService _discoveryService;
    private readonly ScalingOrchestrator _orchestrator;

    private readonly List<AppItemViewModel> _allApps = [];
    private AppItemViewModel? _selectedApp;
    private string _searchQuery = string.Empty;
    private AppListFilterTab _activeTab = AppListFilterTab.All;
    private bool _showSupportedOnly;
    private bool _isLoading;
    private bool _isSettingsOpen;

    private int _draftScalePercent = 125;
    private string _draftScaleText = "125";
    private bool _draftUpdateShortcuts = true;
    private bool _draftAutoApplyOnOpen = true;

    private string _statusMessage = string.Empty;
    private bool _isStatusWarning;
    private int _systemScalePercent = 100;

    public Loc Loc => Loc.Instance;

    public ObservableCollection<AppItemViewModel> FilteredApps { get; } = [];

    public int[] PresetScales { get; } = [80, 90, 100, 110, 115, 125, 135, 150, 175, 200];

    public RelayCommand<string> SetFilterTabCommand { get; }
    public RelayCommand<int> SelectPresetScaleCommand { get; }
    public RelayCommand ApplyScaleCommand { get; }
    public AsyncRelayCommand ApplyAndRestartCommand { get; }
    public RelayCommand LaunchScaledCommand { get; }
    public AsyncRelayCommand ResetScaleCommand { get; }
    public RelayCommand CreateDesktopShortcutCommand { get; }
    public RelayCommand OpenExecutableFolderCommand { get; }
    public RelayCommand BrowseExecutableCommand { get; }
    public AsyncRelayCommand RefreshAppsCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand<string> SetLanguageCommand { get; }
    public RelayCommand ResetAllRulesCommand { get; }

    public event Action? ProfilesChanged;

    public MainViewModel(
        CompatibilityAnalyzer analyzer,
        AppDiscoveryService discoveryService,
        ScalingOrchestrator orchestrator)
    {
        _analyzer = analyzer;
        _discoveryService = discoveryService;
        _orchestrator = orchestrator;

        _systemScalePercent = Win32Interop.GetPrimaryMonitorScalePercent();
        _statusMessage = GetDefaultReadyStatusMessage();

        SetFilterTabCommand = new RelayCommand<string>(OnSetFilterTab);
        SelectPresetScaleCommand = new RelayCommand<int>(OnSelectPresetScale, _ => SelectedApp?.CanScale == true);
        ApplyScaleCommand = new RelayCommand(OnApplyScale, () => SelectedApp?.CanScale == true);
        ApplyAndRestartCommand = new AsyncRelayCommand(OnApplyAndRestartAsync, () => SelectedApp is { CanScale: true, IsRunning: true });
        LaunchScaledCommand = new RelayCommand(OnLaunchScaled, () => SelectedApp?.CanScale == true);
        ResetScaleCommand = new AsyncRelayCommand(OnResetScaleAsync, () => SelectedApp is not null && (SelectedApp.HasCustomScale || DraftScalePercent != 100));
        CreateDesktopShortcutCommand = new RelayCommand(OnCreateDesktopShortcut, () => SelectedApp?.CanScale == true);
        OpenExecutableFolderCommand = new RelayCommand(OnOpenExecutableFolder, () => SelectedApp is not null);
        BrowseExecutableCommand = new RelayCommand(OnBrowseExecutable);
        RefreshAppsCommand = new AsyncRelayCommand(LoadApplicationsAsync);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ToggleLanguageCommand = new RelayCommand(() => ApplyLanguage(Loc.IsTurkish ? "en" : "tr"));
        SetLanguageCommand = new RelayCommand<string>(lang => ApplyLanguage(lang ?? "en"));
        ResetAllRulesCommand = new RelayCommand(OnResetAllRules);
    }

    private static string GetDefaultReadyStatusMessage() => Loc.Pick(
        "Per-application DPI scaling ready while preserving Windows global display scaling.",
        "Windows genel ölçeklendirmesi korunarak uygulama bazlı DPI yönetimi hazır.");

    private void ApplyLanguage(string lang)
    {
        Loc.Instance.SetLanguage(lang);
        _orchestrator.Settings.Language = Loc.Instance.CurrentLanguage;
        _orchestrator.SaveSettings(_orchestrator.Settings);

        foreach (AppItemViewModel item in _allApps)
            item.NotifyLocalizationChanged();

        OnPropertyChanged(nameof(SystemScaleBadgeText));
        OnPropertyChanged(nameof(ActiveRulesBadgeText));
        OnPropertyChanged(nameof(ScaleComparisonText));
        OnPropertyChanged(nameof(PrimaryApplyButtonText));
        OnPropertyChanged(nameof(SelectedApp));
        SetStatus(GetDefaultReadyStatusMessage(), warning: false);
        ProfilesChanged?.Invoke();
    }

    public int SystemScalePercent
    {
        get => _systemScalePercent;
        private set
        {
            if (SetProperty(ref _systemScalePercent, value))
                OnPropertyChanged(nameof(SystemScaleBadgeText));
        }
    }

    public string SystemScaleBadgeText => Loc.IsTurkish
        ? $"Windows Ölçeği: %{SystemScalePercent} (Sabit)"
        : $"Windows Scale: {SystemScalePercent}% (Fixed)";

    public int ActiveRulesCount => _orchestrator.Profiles.Count(p => p.IsEnabled && p.ScalePercent != 100);
    public string ActiveRulesBadgeText => Loc.IsTurkish
        ? $"Aktif Kural: {ActiveRulesCount}"
        : $"Active Rules: {ActiveRulesCount}";

    public int TotalAppsCount => _allApps.Count;
    public int RunningAppsCount => _allApps.Count(a => a.IsRunning);
    public int ConfiguredAppsCount => _allApps.Count(a => a.HasCustomScale);

    public bool IsTabAll => _activeTab == AppListFilterTab.All;
    public bool IsTabRunning => _activeTab == AppListFilterTab.Running;
    public bool IsTabConfigured => _activeTab == AppListFilterTab.Configured;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                RebuildFilteredList();
        }
    }

    public bool ShowSupportedOnly
    {
        get => _showSupportedOnly;
        set
        {
            if (SetProperty(ref _showSupportedOnly, value))
                RebuildFilteredList();
        }
    }

    public AppItemViewModel? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
            {
                LoadDraftFromSelectedApp();
                RaiseSelectionProperties();
            }
        }
    }

    public bool HasSelectedApp => SelectedApp is not null;

    public int DraftScalePercent
    {
        get => _draftScalePercent;
        set
        {
            int clamped = ScaleMath.ClampPercent(value);
            if (SetProperty(ref _draftScalePercent, clamped))
            {
                string txt = clamped.ToString(CultureInfo.InvariantCulture);
                if (_draftScaleText != txt)
                {
                    _draftScaleText = txt;
                    OnPropertyChanged(nameof(DraftScaleText));
                }
                OnPropertyChanged(nameof(ScaleComparisonText));
                OnPropertyChanged(nameof(PreviewBarWidth));
                ResetScaleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DraftScaleText
    {
        get => _draftScaleText;
        set
        {
            if (SetProperty(ref _draftScaleText, value))
            {
                string cleaned = value.Replace("%", string.Empty).Trim();
                if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                    parsed is >= ScaleMath.MinScalePercent and <= ScaleMath.MaxScalePercent)
                {
                    if (_draftScalePercent != parsed)
                    {
                        _draftScalePercent = parsed;
                        OnPropertyChanged(nameof(DraftScalePercent));
                        OnPropertyChanged(nameof(ScaleComparisonText));
                        OnPropertyChanged(nameof(PreviewBarWidth));
                        ResetScaleCommand.RaiseCanExecuteChanged();
                    }
                }
            }
        }
    }

    public string ScaleComparisonText => Loc.IsTurkish
        ? $"Windows: %{SystemScalePercent}  →  {SelectedApp?.DisplayName ?? "Uygulama"}: %{DraftScalePercent}"
        : $"Windows: {SystemScalePercent}%  →  {SelectedApp?.DisplayName ?? "Application"}: {DraftScalePercent}%";

    public double PreviewBarWidth => Math.Clamp(DraftScalePercent * 1.15, 55, 290);

    public bool DraftUpdateShortcuts
    {
        get => _draftUpdateShortcuts;
        set => SetProperty(ref _draftUpdateShortcuts, value);
    }

    public bool DraftAutoApplyOnOpen
    {
        get => _draftAutoApplyOnOpen;
        set => SetProperty(ref _draftAutoApplyOnOpen, value);
    }

    public bool ShowShortcutIntegrationOption =>
        SelectedApp?.Compatibility.SupportsDirectShortcutFlags == true;

    public bool ShowAutoRuntimeOption =>
        SelectedApp?.Compatibility.DeliveryMode == ScalingDeliveryMode.LiveRuntimeDpiMessage;

    public bool ShowRestartAndApplyButton =>
        SelectedApp is { CanScale: true, IsRunning: true } && SelectedApp.Compatibility.RequiresRestart;

    public string PrimaryApplyButtonText =>
        SelectedApp?.Compatibility.DeliveryMode == ScalingDeliveryMode.LiveRuntimeDpiMessage && SelectedApp.IsRunning
            ? Loc.Pick("Apply Live (Instant)", "Canlı Uygula (Anında)")
            : Loc.Pick("Save & Apply", "Kaydet ve Uygula");

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusWarning
    {
        get => _isStatusWarning;
        private set => SetProperty(ref _isStatusWarning, value);
    }

    // App-wide Settings Bindings
    public bool CloseToTray
    {
        get => _orchestrator.Settings.CloseToTray;
        set
        {
            if (_orchestrator.Settings.CloseToTray != value)
            {
                _orchestrator.Settings.CloseToTray = value;
                _orchestrator.SaveSettings(_orchestrator.Settings);
                OnPropertyChanged();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _orchestrator.Settings.MinimizeToTray;
        set
        {
            if (_orchestrator.Settings.MinimizeToTray != value)
            {
                _orchestrator.Settings.MinimizeToTray = value;
                _orchestrator.SaveSettings(_orchestrator.Settings);
                OnPropertyChanged();
            }
        }
    }

    public bool StartWithWindows
    {
        get => StartupManager.IsRegisteredForStartup();
        set
        {
            if (StartupManager.SetStartupRegistration(value, startMinimized: true))
            {
                _orchestrator.Settings.StartWithWindows = value;
                _orchestrator.SaveSettings(_orchestrator.Settings);
                OnPropertyChanged();
                SetStatus(value
                    ? Loc.Pick(
                        "DPIShift will launch automatically in the system tray on Windows startup.",
                        "DPIShift, Windows açılışında sistem tepsisinde otomatik başlayacak.")
                    : Loc.Pick(
                        "Automatic startup on Windows login disabled.",
                        "Windows açılışında otomatik başlatma kapatıldı."), warning: false);
            }
        }
    }

    public bool EnableBackgroundRuntimeScaling
    {
        get => _orchestrator.Settings.EnableBackgroundRuntimeScaling;
        set
        {
            if (_orchestrator.Settings.EnableBackgroundRuntimeScaling != value)
            {
                _orchestrator.Settings.EnableBackgroundRuntimeScaling = value;
                _orchestrator.SaveSettings(_orchestrator.Settings);
                OnPropertyChanged();
                SetStatus(value
                    ? Loc.Pick(
                        "Background live window DPI watcher enabled (0% idle CPU).",
                        "Arka plan canlı pencere DPI izleyicisi etkinleştirildi (0% boşta CPU).")
                    : Loc.Pick(
                        "Background live window DPI watcher paused.",
                        "Arka plan canlı pencere DPI izleyicisi duraklatıldı."), warning: false);
                ProfilesChanged?.Invoke();
            }
        }
    }

    public async Task LoadApplicationsAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        SystemScalePercent = Win32Interop.GetPrimaryMonitorScalePercent();
        string? previousSelectedId = SelectedApp?.Id;

        try
        {
            List<DiscoveredApp> discovered = await _discoveryService.DiscoverAllAsync(_orchestrator.Profiles);
            _allApps.Clear();

            foreach (DiscoveredApp app in discovered)
            {
                AppScaleProfile? profile = _orchestrator.GetProfile(app.Id);
                _allApps.Add(new AppItemViewModel(app, profile));
            }

            RebuildFilteredList();

            if (!string.IsNullOrEmpty(previousSelectedId))
            {
                SelectedApp = FilteredApps.FirstOrDefault(a => string.Equals(a.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                              ?? _allApps.FirstOrDefault(a => string.Equals(a.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                              ?? FilteredApps.FirstOrDefault();
            }
            else if (SelectedApp is null)
            {
                SelectedApp = FilteredApps.FirstOrDefault(a => a.HasCustomScale)
                              ?? FilteredApps.FirstOrDefault(a => a.CanScale)
                              ?? FilteredApps.FirstOrDefault();
            }

            RefreshCounters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshLiveRunningStatus()
    {
        if (_allApps.Count == 0)
            return;

        SystemScalePercent = Win32Interop.GetPrimaryMonitorScalePercent();
        _discoveryService.RefreshRunningStates(_allApps.Select(vm => vm.Model));
        foreach (AppItemViewModel vm in _allApps)
        {
            vm.SyncFromModelAndProfile(_orchestrator.GetProfile(vm.Id));
        }

        RefreshCounters();
        RaiseSelectionProperties();
    }

    private void OnSetFilterTab(string? tabName)
    {
        _activeTab = tabName switch
        {
            "Running" => AppListFilterTab.Running,
            "Configured" => AppListFilterTab.Configured,
            _ => AppListFilterTab.All
        };

        OnPropertyChanged(nameof(IsTabAll));
        OnPropertyChanged(nameof(IsTabRunning));
        OnPropertyChanged(nameof(IsTabConfigured));

        if (_activeTab == AppListFilterTab.Running)
            RefreshLiveRunningStatus();

        RebuildFilteredList();
    }

    private void OnSelectPresetScale(int preset)
    {
        if (preset > 0)
            DraftScalePercent = preset;
    }

    private void OnApplyScale()
    {
        if (SelectedApp is null)
            return;

        RefreshLiveRunningStatus();

        ScalingActionResult result = _orchestrator.ApplyProfile(
            SelectedApp.Model,
            DraftScalePercent,
            DraftUpdateShortcuts,
            DraftAutoApplyOnOpen);

        AppScaleProfile? updatedProfile = _orchestrator.GetProfile(SelectedApp.Id);
        SelectedApp.SyncFromModelAndProfile(updatedProfile);
        RefreshCounters();
        RaiseSelectionProperties();
        ProfilesChanged?.Invoke();

        SetStatus(result.Message, warning: !result.Success || result.NeedsRestartToTakeEffect);
    }

    private async Task OnApplyAndRestartAsync()
    {
        if (SelectedApp is null || !SelectedApp.CanScale)
            return;

        AppItemViewModel targetVm = SelectedApp;

        MessageBoxResult confirm = MessageBox.Show(
            Loc.Pick(
                $"{targetVm.DisplayName} is currently running.\n\nTo activate the {DraftScalePercent}% interface scale, all open windows and background processes of the application will be closed and restarted. Please make sure your unsaved work is saved.\n\nRestart now?",
                $"{targetVm.DisplayName} şu anda çalışıyor.\n\n%{DraftScalePercent} arayüz ölçeğinin etkinleşmesi için uygulamanın tüm açık pencereleri ve arka plan süreçleri kapatılıp yeniden başlatılacak. Kaydedilmemiş çalışmalarınızı kaydettiğinizden emin olun.\n\nŞimdi yeniden başlatılsın mı?"),
            Loc.Pick("DPIShift — Restart & Apply Scale", "DPIShift — Yeniden Başlat ve Ölçeklendir"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        ScalingActionResult applyRes = _orchestrator.ApplyProfile(
            targetVm.Model,
            DraftScalePercent,
            DraftUpdateShortcuts,
            DraftAutoApplyOnOpen);

        AppScaleProfile? profile = _orchestrator.GetProfile(targetVm.Id);
        targetVm.SyncFromModelAndProfile(profile);
        RefreshCounters();
        ProfilesChanged?.Invoke();

        if (profile is null)
        {
            // User picked 100% -> restart at default 100%
            var tempDefault = CreateAdHocProfile(targetVm, 100);
            SetStatus(
                Loc.Pick(
                    $"Restarting {targetVm.DisplayName} at default (100%) scale...",
                    $"{targetVm.DisplayName} varsayılan (%100) ölçekle yeniden başlatılıyor..."),
                warning: false);
            bool okDefault = await ScaledProcessLauncher.RestartAtDefaultScaleAsync(tempDefault);
            await Task.Delay(500);
            RefreshLiveRunningStatus();
            SetStatus(okDefault
                ? Loc.Pick(
                    $"{targetVm.DisplayName} restarted at default (100%) scale.",
                    $"{targetVm.DisplayName} varsayılan (%100) ölçekte yeniden başlatıldı.")
                : applyRes.Message, warning: !okDefault);
            return;
        }

        SetStatus(
            Loc.Pick(
                $"Closing {targetVm.DisplayName} and restarting at {profile.ScalePercent}% scale...",
                $"{targetVm.DisplayName} kapatılıyor ve %{profile.ScalePercent} ölçeğiyle yeniden başlatılıyor..."),
            warning: false);
        bool launched = await ScaledProcessLauncher.RestartAndLaunchScaledAsync(profile);
        await Task.Delay(600);
        RefreshLiveRunningStatus();

        SetStatus(launched
            ? Loc.Pick(
                $"{targetVm.DisplayName} restarted at {profile.ScalePercent}% true vector UI scale.",
                $"{targetVm.DisplayName} %{profile.ScalePercent} gerçek arayüz ölçeğiyle yeniden başlatıldı.")
            : Loc.Pick(
                $"{targetVm.DisplayName} was closed, but could not be started automatically. Click 'Launch Scaled' to start it.",
                $"{targetVm.DisplayName} kapatıldı ancak otomatik başlatılamadı. 'Ölçekli Başlat' ile çalıştırabilirsiniz."),
            warning: !launched);
    }

    private void OnLaunchScaled()
    {
        if (SelectedApp is null || !SelectedApp.CanScale)
            return;

        // Save rule first if not saved yet or modified
        if (DraftScalePercent != 100 && SelectedApp.ConfiguredScalePercent != DraftScalePercent)
        {
            _orchestrator.ApplyProfile(
                SelectedApp.Model,
                DraftScalePercent,
                DraftUpdateShortcuts,
                DraftAutoApplyOnOpen);
            SelectedApp.SyncFromModelAndProfile(_orchestrator.GetProfile(SelectedApp.Id));
            RefreshCounters();
            ProfilesChanged?.Invoke();
        }

        RefreshLiveRunningStatus();
        if (SelectedApp.IsRunning && SelectedApp.Compatibility.RequiresRestart)
        {
            ApplyAndRestartCommand.Execute(null);
            return;
        }

        AppScaleProfile profile = _orchestrator.GetProfile(SelectedApp.Id)
                                  ?? CreateAdHocProfile(SelectedApp, DraftScalePercent);

        bool ok = ScaledProcessLauncher.LaunchScaled(profile);
        SetStatus(ok
            ? Loc.Pick(
                $"Launched {SelectedApp.DisplayName} at {profile.ScalePercent}% scale.",
                $"{SelectedApp.DisplayName} %{profile.ScalePercent} ölçeğiyle başlatıldı.")
            : Loc.Pick(
                $"Could not launch {SelectedApp.DisplayName}. Please check the executable path.",
                $"{SelectedApp.DisplayName} başlatılamadı. Dosya yolunu kontrol edin."),
            warning: !ok);
    }

    private async Task OnResetScaleAsync()
    {
        if (SelectedApp is null)
            return;

        AppItemViewModel targetVm = SelectedApp;
        bool wasRunning = targetVm.IsRunning;
        bool requiresRestart = targetVm.Compatibility.RequiresRestart;
        AppScaleProfile restartTemplate = _orchestrator.GetProfile(targetVm.Id) ?? CreateAdHocProfile(targetVm, 100);

        ScalingActionResult res = _orchestrator.ResetProfile(targetVm.Id, targetVm.ExecutablePath);
        targetVm.SyncFromModelAndProfile(null);
        DraftScalePercent = 100;
        RefreshCounters();
        RebuildFilteredList();
        RaiseSelectionProperties();
        ProfilesChanged?.Invoke();

        SetStatus(res.Message, warning: false);

        if (wasRunning && requiresRestart)
        {
            MessageBoxResult ask = MessageBox.Show(
                Loc.Pick(
                    $"All scaling rules and shortcuts for {targetVm.DisplayName} have been restored to default.\n\nWould you like to restart the running application now so it returns to 100% default scale immediately?",
                    $"{targetVm.DisplayName} için tüm ölçek ayarları ve kısayollar varsayılana döndürüldü.\n\nAçık olan uygulamanın hemen %100 varsayılan ölçeğe dönmesi için şimdi yeniden başlatmak ister misiniz?"),
                Loc.Pick("DPIShift — Restart at Default Scale", "DPIShift — Varsayılan Ölçekle Yeniden Başlat"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ask == MessageBoxResult.Yes)
            {
                bool ok = await ScaledProcessLauncher.RestartAtDefaultScaleAsync(restartTemplate);
                await Task.Delay(500);
                RefreshLiveRunningStatus();
                if (ok)
                {
                    SetStatus(
                        Loc.Pick(
                            $"{targetVm.DisplayName} restarted at default (100%) scale.",
                            $"{targetVm.DisplayName} varsayılan (%100) ölçekle yeniden başlatıldı."),
                        warning: false);
                }
            }
        }
    }

    private void OnCreateDesktopShortcut()
    {
        if (SelectedApp is null || !SelectedApp.CanScale)
            return;

        if (_orchestrator.GetProfile(SelectedApp.Id) is null || SelectedApp.ConfiguredScalePercent != DraftScalePercent)
        {
            _orchestrator.ApplyProfile(
                SelectedApp.Model,
                DraftScalePercent == 100 ? 125 : DraftScalePercent,
                DraftUpdateShortcuts,
                DraftAutoApplyOnOpen);
            SelectedApp.SyncFromModelAndProfile(_orchestrator.GetProfile(SelectedApp.Id));
            RefreshCounters();
            ProfilesChanged?.Invoke();
        }

        string? lnk = _orchestrator.CreateDesktopLauncherForProfile(SelectedApp.Id);
        SetStatus(!string.IsNullOrEmpty(lnk)
            ? Loc.Pick(
                $"Created scaled desktop shortcut: {Path.GetFileName(lnk)}",
                $"Masaüstüne ölçekli kısayol oluşturuldu: {Path.GetFileName(lnk)}")
            : Loc.Pick(
                "Could not create desktop shortcut.",
                "Masaüstü kısayolu oluşturulamadı."),
            warning: string.IsNullOrEmpty(lnk));
    }

    private void OnOpenExecutableFolder()
    {
        if (SelectedApp is null || !File.Exists(SelectedApp.ExecutablePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{SelectedApp.ExecutablePath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnBrowseExecutable()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.Pick("Select Application to Scale (.exe)", "Ölçeklendirilecek Uygulamayı Seçin (.exe)"),
            Filter = Loc.Pick("Executables (*.exe)|*.exe", "Uygulamalar (*.exe)|*.exe"),
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != true)
            return;

        string chosenPath = dlg.FileName;
        CompatibilityReport report = _analyzer.Analyze(chosenPath);

        string effectivePath = chosenPath;
        if (report.Technology == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrEmpty(report.SquirrelRootDir) &&
            !string.IsNullOrEmpty(report.SquirrelProcessName))
        {
            effectivePath = CompatibilityAnalyzer.ResolveLatestSquirrelExecutable(report.SquirrelRootDir, report.SquirrelProcessName)
                            ?? chosenPath;
        }

        string id = CompatibilityAnalyzer.NormalizeCanonicalId(effectivePath, report);
        AppItemViewModel? existing = _allApps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            string title = Path.GetFileNameWithoutExtension(effectivePath);
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(effectivePath);
                if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                    title = fvi.FileDescription.Trim();
                else if (!string.IsNullOrWhiteSpace(fvi.ProductName))
                    title = fvi.ProductName.Trim();
            }
            catch { }

            var model = new DiscoveredApp
            {
                Id = id,
                DisplayName = title,
                ExecutablePath = effectivePath,
                IsInstalled = true,
                Compatibility = report
            };
            existing = new AppItemViewModel(model, _orchestrator.GetProfile(id));
            _allApps.Insert(0, existing);
        }

        _activeTab = AppListFilterTab.All;
        ShowSupportedOnly = false;
        SearchQuery = string.Empty;
        OnPropertyChanged(nameof(IsTabAll));
        OnPropertyChanged(nameof(IsTabRunning));
        OnPropertyChanged(nameof(IsTabConfigured));
        RebuildFilteredList();
        RefreshCounters();
        SelectedApp = existing;

        SetStatus(
            Loc.Pick(
                $"Analyzed {existing.DisplayName}: {report.TechnologyBadge} ({report.SupportBadgeText}).",
                $"{existing.DisplayName} analiz edildi: {report.TechnologyBadge} ({report.SupportBadgeText})."),
            warning: !report.CanScale);
    }

    private void OnResetAllRules()
    {
        if (_orchestrator.Profiles.Count == 0)
        {
            SetStatus(
                Loc.Pick("No active application scaling rules to reset.", "Sıfırlanacak aktif uygulama kuralı bulunmuyor."),
                warning: false);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            Loc.Pick(
                "All saved application scaling rules will be removed, and modified shortcuts, .vmoptions files, and live window DPI values will be restored to their original defaults.\n\nContinue?",
                "Kaydedilmiş tüm uygulama ölçeklendirme kuralları kaldırılacak; güncellenmiş kısayollar, .vmoptions ayarları ve canlı pencere DPI değerleri orijinal varsayılanlarına döndürülecek.\n\nDevam edilsin mi?"),
            Loc.Pick("DPIShift — Reset All Rules", "DPIShift — Tüm Kuralları Sıfırla"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        int count = _orchestrator.ResetAllProfiles();
        foreach (AppItemViewModel vm in _allApps)
        {
            vm.SyncFromModelAndProfile(null);
        }

        DraftScalePercent = 100;
        RefreshCounters();
        RebuildFilteredList();
        RaiseSelectionProperties();
        ProfilesChanged?.Invoke();

        SetStatus(
            Loc.Pick(
                $"Reset {count} application rule(s) and restored default behavior.",
                $"{count} uygulama kuralı tamamen sıfırlandı ve varsayılan davranışına döndürüldü."),
            warning: false);
    }

    private void LoadDraftFromSelectedApp()
    {
        if (SelectedApp is null)
            return;

        AppScaleProfile? profile = _orchestrator.GetProfile(SelectedApp.Id);
        if (profile is not null)
        {
            DraftScalePercent = profile.ScalePercent;
            DraftUpdateShortcuts = profile.UpdateUserShortcuts;
            DraftAutoApplyOnOpen = profile.AutoApplyOnWindowOpen;
        }
        else
        {
            DraftScalePercent = SelectedApp.CanScale ? 125 : 100;
            DraftUpdateShortcuts = true;
            DraftAutoApplyOnOpen = true;
        }
    }

    private void RebuildFilteredList()
    {
        string q = _searchQuery.Trim();
        IEnumerable<AppItemViewModel> query = _allApps;

        query = _activeTab switch
        {
            AppListFilterTab.Running => query.Where(a => a.IsRunning),
            AppListFilterTab.Configured => query.Where(a => a.HasCustomScale),
            _ => query
        };

        if (_showSupportedOnly)
            query = query.Where(a => a.CanScale);

        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(a =>
                a.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                a.ExecutableFileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.TechnologyBadge.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        }

        List<AppItemViewModel> ordered = query
            .OrderByDescending(a => a.HasCustomScale)
            .ThenByDescending(a => a.Support == SupportLevel.Full)
            .ThenByDescending(a => a.Support == SupportLevel.Partial)
            .ThenByDescending(a => a.IsRunning)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FilteredApps.Clear();
        foreach (AppItemViewModel item in ordered)
            FilteredApps.Add(item);
    }

    private void RefreshCounters()
    {
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(ActiveRulesBadgeText));
        OnPropertyChanged(nameof(TotalAppsCount));
        OnPropertyChanged(nameof(RunningAppsCount));
        OnPropertyChanged(nameof(ConfiguredAppsCount));
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(HasSelectedApp));
        OnPropertyChanged(nameof(ShowShortcutIntegrationOption));
        OnPropertyChanged(nameof(ShowAutoRuntimeOption));
        OnPropertyChanged(nameof(ShowRestartAndApplyButton));
        OnPropertyChanged(nameof(PrimaryApplyButtonText));
        OnPropertyChanged(nameof(ScaleComparisonText));
        OnPropertyChanged(nameof(PreviewBarWidth));

        SelectPresetScaleCommand.RaiseCanExecuteChanged();
        ApplyScaleCommand.RaiseCanExecuteChanged();
        ApplyAndRestartCommand.RaiseCanExecuteChanged();
        LaunchScaledCommand.RaiseCanExecuteChanged();
        ResetScaleCommand.RaiseCanExecuteChanged();
        CreateDesktopShortcutCommand.RaiseCanExecuteChanged();
        OpenExecutableFolderCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string message, bool warning)
    {
        StatusMessage = message;
        IsStatusWarning = warning;
    }

    private static AppScaleProfile CreateAdHocProfile(AppItemViewModel vm, int scalePercent) => new()
    {
        Id = vm.Id,
        DisplayName = vm.DisplayName,
        ExecutablePath = vm.ExecutablePath,
        ScalePercent = scalePercent,
        IsEnabled = scalePercent != 100,
        DetectedTech = vm.Compatibility.Technology,
        DeliveryMode = vm.Compatibility.DeliveryMode,
        SquirrelUpdateExePath = vm.Compatibility.SquirrelUpdateExePath,
        SquirrelProcessName = vm.Compatibility.SquirrelProcessName,
        JetBrainsVmOptionsPath = vm.Compatibility.JetBrainsVmOptionsPath,
        EclipseIniPath = vm.Compatibility.EclipseIniPath
    };
}
