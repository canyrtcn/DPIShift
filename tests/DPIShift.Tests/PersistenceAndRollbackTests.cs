using DPIShift.Core.Models;
using DPIShift.Core.Native;
using DPIShift.Core.Persistence;
using DPIShift.Core.Scaling;

namespace DPIShift.Tests;

public sealed class PersistenceAndRollbackTests : IDisposable
{
    private readonly string _tempRoot;

    public PersistenceAndRollbackTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DPIShiftRollback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void ShortcutIntegration_PatchesIdempotently_AndRestoresExactOriginalOnReset()
    {
        string fakeChromeDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "Chrome")).FullName;
        string fakeChromeExe = Path.Combine(fakeChromeDir, "chrome.exe");
        File.WriteAllBytes(fakeChromeExe, new byte[128]);

        string shortcutsDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "Shortcuts")).FullName;
        string lnkPath = Path.Combine(shortcutsDir, "Google Chrome.lnk");

        bool created = ShellLinkInterop.TryWriteShortcut(new ShellLinkInfo
        {
            ShortcutPath = lnkPath,
            TargetPath = fakeChromeExe,
            Arguments = "--profile-directory=\"Default\"",
            WorkingDirectory = fakeChromeDir,
            Description = "Chrome Test"
        });
        Assert.True(created);

        var service = new ShortcutIntegrationService(
            customSearchFolders: [shortcutsDir],
            enableRegistryRunIntegration: false);

        var profile = new AppScaleProfile
        {
            Id = fakeChromeExe.ToLowerInvariant(),
            DisplayName = "Google Chrome",
            ExecutablePath = fakeChromeExe,
            ScalePercent = 125,
            UpdateUserShortcuts = true,
            DetectedTech = ScalingTech.Chromium,
            DeliveryMode = ScalingDeliveryMode.ChromiumFlags
        };

        // 1. Apply 125%
        int updated = service.ApplyUserShortcutsAndStartup(profile);
        Assert.Equal(1, updated);

        ShellLinkInfo? after125 = ShellLinkInterop.TryReadShortcut(lnkPath);
        Assert.NotNull(after125);
        Assert.Contains("--profile-directory=\"Default\"", after125!.Arguments);
        Assert.Contains("--force-device-scale-factor=1.25", after125.Arguments);

        // 2. Update to 150% (must NOT corrupt backup or duplicate flags!)
        profile.ScalePercent = 150;
        service.ApplyUserShortcutsAndStartup(profile);

        ShellLinkInfo? after150 = ShellLinkInterop.TryReadShortcut(lnkPath);
        Assert.NotNull(after150);
        Assert.DoesNotContain("1.25", after150!.Arguments);
        Assert.Contains("--force-device-scale-factor=1.5", after150.Arguments);

        // 3. Revert -> must restore exact "--profile-directory=\"Default\""
        service.RevertShortcutsAndStartup(profile);
        ShellLinkInfo? restored = ShellLinkInterop.TryReadShortcut(lnkPath);
        Assert.NotNull(restored);
        Assert.Equal("--profile-directory=\"Default\"", restored!.Arguments);
    }

    [Fact]
    public void JetBrainsVmOptions_PreservesCustomFlags_AndRevertsCleanly()
    {
        string jbConfigDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "DataGrip2026.1")).FullName;
        string backupsDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "backups")).FullName;
        string vmPath = Path.Combine(jbConfigDir, "datagrip64.exe.vmoptions");

        File.WriteAllLines(vmPath, ["-Xms512m", "-Xmx4096m"]);

        var profile = new AppScaleProfile
        {
            Id = "datagrip_test",
            DisplayName = "DataGrip",
            ScalePercent = 150,
            DeliveryMode = ScalingDeliveryMode.JetBrainsVmOptions,
            JetBrainsVmOptionsPath = vmPath
        };

        Assert.True(JetBrainsConfigManager.ApplyVmOptionsScale(profile, 150, backupsDir));
        string content = File.ReadAllText(vmPath);
        Assert.Contains("-Xmx4096m", content);
        Assert.Contains("-Dsun.java2d.uiScale=1.5", content);

        Assert.True(JetBrainsConfigManager.RevertVmOptionsScale(profile, backupsDir));
        string reverted = File.ReadAllText(vmPath);
        Assert.Contains("-Xms512m", reverted);
        Assert.Contains("-Xmx4096m", reverted);
        Assert.DoesNotContain("sun.java2d.uiScale", reverted);
    }

    [Fact]
    public void Orchestrator_PersistsProfilesAtomically_AndResetsAll()
    {
        string storeDir = Path.Combine(_tempRoot, "AppDataStore");
        string shortcutsDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "UserShortcuts")).FullName;
        string appExe = Path.Combine(_tempRoot, "app.exe");
        File.WriteAllBytes(appExe, new byte[128]);

        var store = new ConfigStore(storeDir);
        var shortcuts = new ShortcutIntegrationService([shortcutsDir], enableRegistryRunIntegration: false);
        using var watcher = new RuntimeDpiWatcher();
        var orchestrator = new ScalingOrchestrator(store, shortcuts, watcher);

        var discovered = new DiscoveredApp
        {
            Id = appExe.ToLowerInvariant(),
            DisplayName = "Sample Electron App",
            ExecutablePath = appExe,
            Compatibility = new CompatibilityReport
            {
                Technology = ScalingTech.Electron,
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.ChromiumFlags,
                SupportsDirectShortcutFlags = true
            }
        };

        ScalingActionResult res = orchestrator.ApplyProfile(discovered, 135, updateUserShortcuts: true, autoApplyOnWindowOpen: false);
        Assert.True(res.Success);

        // Reload from disk in a brand-new ConfigStore instance
        DpiShiftConfiguration reloaded = new ConfigStore(storeDir).Load();
        Assert.Single(reloaded.Profiles);
        Assert.Equal(135, reloaded.Profiles[0].ScalePercent);

        int resetCount = orchestrator.ResetAllProfiles();
        Assert.Equal(1, resetCount);
        Assert.Empty(new ConfigStore(storeDir).Load().Profiles);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { }
    }
}
