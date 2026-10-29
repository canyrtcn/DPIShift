using DPIShift.Core.Localization;
using DPIShift.Core.Models;
using DPIShift.Core.Persistence;

namespace DPIShift.Core.Scaling;

public sealed class ScalingActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int UpdatedShortcutCount { get; init; }
    public int LiveScaledWindowCount { get; init; }
    public bool NeedsRestartToTakeEffect { get; init; }
}

public sealed class ScalingOrchestrator
{
    private readonly ConfigStore _configStore;
    private readonly ShortcutIntegrationService _shortcutService;
    private readonly RuntimeDpiWatcher _runtimeWatcher;
    private DpiShiftConfiguration _configuration;

    public ScalingOrchestrator(
        ConfigStore configStore,
        ShortcutIntegrationService shortcutService,
        RuntimeDpiWatcher runtimeWatcher)
    {
        _configStore = configStore;
        _shortcutService = shortcutService;
        _runtimeWatcher = runtimeWatcher;
        _configuration = _configStore.Load();
        Loc.Instance.SetLanguage(_configuration.Settings.Language);
    }

    public DpiShiftConfiguration Configuration => _configuration;
    public AppSettings Settings => _configuration.Settings;
    public IReadOnlyList<AppScaleProfile> Profiles => _configuration.Profiles;

    public void InitializeRuntimeHooks()
    {
        _runtimeWatcher.UpdateActiveProfiles(_configuration.Profiles, _configuration.Settings.EnableBackgroundRuntimeScaling);
    }

    public AppScaleProfile? GetProfile(string appId) =>
        _configuration.Profiles.FirstOrDefault(p => string.Equals(p.Id, appId, StringComparison.OrdinalIgnoreCase));

    public void SaveSettings(AppSettings updatedSettings)
    {
        _configuration.Settings = updatedSettings;
        _configStore.Save(_configuration);
        _runtimeWatcher.UpdateActiveProfiles(_configuration.Profiles, _configuration.Settings.EnableBackgroundRuntimeScaling);
    }

    public ScalingActionResult ApplyProfile(
        DiscoveredApp app,
        int scalePercent,
        bool updateUserShortcuts,
        bool autoApplyOnWindowOpen)
    {
        if (!app.Compatibility.CanScale)
        {
            return new ScalingActionResult
            {
                Success = false,
                Message = Loc.Pick(
                    "This application framework does not support independent vector DPI scaling.",
                    "Bu uygulama teknolojisi bağımsız vektörel DPI ölçeklendirmesini desteklemiyor.")
            };
        }

        int clampedScale = ScaleMath.ClampPercent(scalePercent);
        if (clampedScale == 100)
        {
            return ResetProfile(app.Id, app.ExecutablePath);
        }

        AppScaleProfile? profile = GetProfile(app.Id);
        if (profile is null)
        {
            profile = new AppScaleProfile
            {
                Id = app.Id
            };
            _configuration.Profiles.Add(profile);
        }

        // If user turned off shortcut integration after previously enabling it, revert modified shortcuts cleanly
        if (!updateUserShortcuts && (profile.ModifiedShortcuts.Count > 0 || profile.ModifiedRunEntries.Count > 0))
        {
            _shortcutService.RevertShortcutsAndStartup(profile);
        }

        profile.DisplayName = app.DisplayName;
        profile.ExecutablePath = app.ExecutablePath;
        profile.ScalePercent = clampedScale;
        profile.IsEnabled = true;
        profile.UpdateUserShortcuts = updateUserShortcuts;
        profile.AutoApplyOnWindowOpen = autoApplyOnWindowOpen;
        profile.DetectedTech = app.Compatibility.Technology;
        profile.DeliveryMode = app.Compatibility.DeliveryMode;
        profile.SquirrelUpdateExePath = app.Compatibility.SquirrelUpdateExePath;
        profile.SquirrelProcessName = app.Compatibility.SquirrelProcessName;
        profile.JetBrainsVmOptionsPath = app.Compatibility.JetBrainsVmOptionsPath;
        profile.EclipseIniPath = app.Compatibility.EclipseIniPath;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        int updatedShortcuts = 0;
        int liveWindows = 0;
        bool configWritten = false;

        switch (profile.DeliveryMode)
        {
            case ScalingDeliveryMode.ChromiumFlags:
            case ScalingDeliveryMode.EclipseIniOrArgs:
                if (updateUserShortcuts)
                    updatedShortcuts = _shortcutService.ApplyUserShortcutsAndStartup(profile);
                break;

            case ScalingDeliveryMode.JetBrainsVmOptions:
                configWritten = JetBrainsConfigManager.ApplyVmOptionsScale(profile, clampedScale, _configStore.BackupsDirectory);
                break;

            case ScalingDeliveryMode.LiveRuntimeDpiMessage:
                liveWindows = RuntimeDpiWatcher.ApplyLiveDpiToRunningExecutable(profile.ExecutablePath, clampedScale);
                break;
        }

        _configStore.Save(_configuration);
        _runtimeWatcher.UpdateActiveProfiles(_configuration.Profiles, _configuration.Settings.EnableBackgroundRuntimeScaling);

        bool isRunningNow = ScaledProcessLauncher.FindMatchingProcessIds(profile).Count > 0;
        bool needsRestart = app.Compatibility.RequiresRestart && isRunningNow;

        string statusMsg = BuildSuccessMessage(app, clampedScale, updatedShortcuts, liveWindows, configWritten, needsRestart);

        return new ScalingActionResult
        {
            Success = true,
            Message = statusMsg,
            UpdatedShortcutCount = updatedShortcuts,
            LiveScaledWindowCount = liveWindows,
            NeedsRestartToTakeEffect = needsRestart
        };
    }

    public ScalingActionResult ResetProfile(string appId, string? fallbackExePath = null)
    {
        AppScaleProfile? profile = GetProfile(appId);
        string? exePath = profile?.ExecutablePath ?? fallbackExePath;

        if (profile is not null)
        {
            _shortcutService.RevertShortcutsAndStartup(profile);

            if (profile.DeliveryMode == ScalingDeliveryMode.JetBrainsVmOptions ||
                !string.IsNullOrEmpty(profile.ModifiedConfigFilePath))
            {
                JetBrainsConfigManager.RevertVmOptionsScale(profile, _configStore.BackupsDirectory);
            }

            if (profile.DeliveryMode == ScalingDeliveryMode.LiveRuntimeDpiMessage &&
                !string.IsNullOrWhiteSpace(profile.ExecutablePath))
            {
                RuntimeDpiWatcher.ResetLiveDpiForRunningExecutable(profile.ExecutablePath);
            }

            _configuration.Profiles.Remove(profile);
            _configStore.Save(_configuration);
            _runtimeWatcher.UpdateActiveProfiles(_configuration.Profiles, _configuration.Settings.EnableBackgroundRuntimeScaling);
        }
        else if (!string.IsNullOrWhiteSpace(exePath))
        {
            RuntimeDpiWatcher.ResetLiveDpiForRunningExecutable(exePath);
        }

        return new ScalingActionResult
        {
            Success = true,
            Message = Loc.Pick(
                "Scaling rule removed and application restored to default (100% / System) behavior.",
                "Ölçeklendirme kuralı kaldırıldı ve uygulama varsayılan (%100 / Sistem) davranışına döndürüldü.")
        };
    }

    public int ResetAllProfiles()
    {
        List<AppScaleProfile> snapshot = _configuration.Profiles.ToList();
        int count = snapshot.Count;
        foreach (AppScaleProfile p in snapshot)
        {
            ResetProfile(p.Id, p.ExecutablePath);
        }
        return count;
    }

    public string? CreateDesktopLauncherForProfile(string appId)
    {
        AppScaleProfile? profile = GetProfile(appId);
        if (profile is null)
            return null;

        string? lnk = _shortcutService.CreateScaledDesktopShortcut(profile);
        if (!string.IsNullOrEmpty(lnk))
            _configStore.Save(_configuration);

        return lnk;
    }

    private static string BuildSuccessMessage(
        DiscoveredApp app,
        int scalePercent,
        int updatedShortcuts,
        int liveWindows,
        bool configWritten,
        bool needsRestart)
    {
        if (app.Compatibility.DeliveryMode == ScalingDeliveryMode.LiveRuntimeDpiMessage)
        {
            return liveWindows > 0
                ? Loc.Pick(
                    $"{scalePercent}% vector DPI scale applied immediately to {liveWindows} open window(s).",
                    $"%{scalePercent} vektörel DPI ölçeği açık olan {liveWindows} pencereye anında uygulandı.")
                : Loc.Pick(
                    $"{scalePercent}% rule saved. It will be applied automatically when {app.DisplayName} opens.",
                    $"%{scalePercent} kuralı kaydedildi. {app.DisplayName} açıldığında otomatik uygulanacak.");
        }

        if (app.Compatibility.DeliveryMode == ScalingDeliveryMode.JetBrainsVmOptions)
        {
            string baseEn = configWritten
                ? $"{app.DisplayName} .vmoptions configuration updated to {scalePercent}% HiDPI."
                : $"{scalePercent}% rule saved for {app.DisplayName}.";
            string baseTr = configWritten
                ? $"{app.DisplayName} .vmoptions yapılandırması %{scalePercent} HiDPI olarak güncellendi."
                : $"{app.DisplayName} için %{scalePercent} kuralı kaydedildi.";

            return needsRestart
                ? Loc.Pick(
                    $"{baseEn} Restart the application for changes to take effect.",
                    $"{baseTr} Değişikliğin aktif olması için uygulamayı yeniden başlatın.")
                : Loc.Pick(
                    $"{baseEn} It will launch at {scalePercent}% scale next time.",
                    $"{baseTr} Sonraki açılışta %{scalePercent} ölçeğinde başlayacak.");
        }

        if (updatedShortcuts > 0)
        {
            return needsRestart
                ? Loc.Pick(
                    $"{scalePercent}% scale saved ({updatedShortcuts} shortcut(s) updated). Restart the application to apply.",
                    $"%{scalePercent} ölçeği kaydedildi ({updatedShortcuts} kısayol güncellendi). Aktif olması için uygulamayı yeniden başlatın.")
                : Loc.Pick(
                    $"{scalePercent}% scale saved and integrated into {updatedShortcuts} shortcut(s).",
                    $"%{scalePercent} ölçeği kaydedildi ve {updatedShortcuts} kısayola entegre edildi.");
        }

        return needsRestart
            ? Loc.Pick(
                $"{scalePercent}% scale saved. Since the app is running, click 'Apply & Restart Now' to activate immediately.",
                $"%{scalePercent} ölçeği kaydedildi. Uygulama açık olduğu için 'Yeniden Başlat ve Uygula' ile hemen etkinleştirebilirsiniz.")
            : Loc.Pick(
                $"{scalePercent}% scale saved. Use 'Launch Scaled' or a desktop shortcut to start it.",
                $"%{scalePercent} ölçeği kaydedildi. 'Ölçekli Başlat' veya masaüstü kısayolu ile çalıştırabilirsiniz.");
    }
}
