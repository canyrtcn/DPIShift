using System.Diagnostics;
using DPIShift.Core.Models;
using DPIShift.Core.Native;
using Microsoft.Win32;

namespace DPIShift.Core.Detection;

public sealed class AppDiscoveryService
{
    private readonly CompatibilityAnalyzer _analyzer;

    private static readonly string[] IgnoredNameKeywords =
    [
        "uninstall", "unins000", "kaldır", "remove", "setup", "installer",
        "updater", "crashpad", "crashreporter", "helper", "readme",
        "documentation", "release notes", "license", "website"
    ];

    public AppDiscoveryService(CompatibilityAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public Task<List<DiscoveredApp>> DiscoverAllAsync(IEnumerable<AppScaleProfile>? configuredProfiles = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => DiscoverAll(configuredProfiles, cancellationToken), cancellationToken);

    public List<DiscoveredApp> DiscoverAll(IEnumerable<AppScaleProfile>? configuredProfiles = null, CancellationToken cancellationToken = default)
    {
        var byCanonicalId = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        // 1. Discover currently running top-level GUI windows (ultra-fast via Win32 EnumWindows)
        var runningSnapshot = CollectRunningWindowsSnapshot();
        foreach (var entry in runningSnapshot.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpsertCandidate(
                byCanonicalId,
                entry.ExePath,
                preferredName: null,
                isInstalled: false,
                isRunning: true,
                pids: entry.Pids,
                hwnds: entry.Windows);
        }

        // 2. Discover Start Menu & Desktop shortcuts (highest-fidelity user-facing app list!)
        foreach ((string folder, bool recurse) in GetShortcutSearchFolders())
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            foreach (string lnk in lnkFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string shortcutTitle = Path.GetFileNameWithoutExtension(lnk);
                if (IsNoiseShortcutTitle(shortcutTitle))
                    continue;

                ShellLinkInfo? info = ShellLinkInterop.TryReadShortcut(lnk);
                if (info is null || string.IsNullOrWhiteSpace(info.TargetPath))
                    continue;

                // Skip DPIShift's own generated launcher shortcuts
                if (info.Arguments.Contains("--launch", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(info.TargetPath).Equals("DPIShift.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string targetExe = ResolveEffectiveTargetExecutable(info.TargetPath, info.Arguments);
                if (!IsValidCandidateExecutable(targetExe))
                    continue;

                UpsertCandidate(
                    byCanonicalId,
                    targetExe,
                    preferredName: CleanShortcutTitle(shortcutTitle),
                    isInstalled: true,
                    isRunning: false,
                    pids: null,
                    hwnds: null);
            }
        }

        // 3. Supplement from Windows Uninstall Registry (catches apps without Start Menu .lnk)
        DiscoverFromUninstallRegistry(byCanonicalId, cancellationToken);

        // 4. Ensure all user-configured profiles are always present & check background running processes for them
        if (configuredProfiles is not null)
        {
            foreach (AppScaleProfile profile in configuredProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string exeToCheck = ResolveProfileExecutablePath(profile);
                if (!string.IsNullOrWhiteSpace(exeToCheck) && File.Exists(exeToCheck))
                {
                    UpsertCandidate(
                        byCanonicalId,
                        exeToCheck,
                        preferredName: profile.DisplayName,
                        isInstalled: true,
                        isRunning: false,
                        pids: null,
                        hwnds: null);
                }
            }
        }

        // 5. Enrich running state for apps running in tray without a visible top-level window (e.g., Discord in tray, Chrome background)
        EnrichBackgroundProcessStatus(byCanonicalId);

        return byCanonicalId.Values
            .OrderByDescending(a => a.Compatibility.Support == SupportLevel.Full)
            .ThenByDescending(a => a.Compatibility.Support == SupportLevel.Partial)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void RefreshRunningStates(IEnumerable<DiscoveredApp> apps)
    {
        var list = apps.ToList();
        var runningSnapshot = CollectRunningWindowsSnapshot();

        var windowByCanonical = new Dictionary<string, RunningAppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in runningSnapshot.Values)
        {
            CompatibilityReport rep = _analyzer.Analyze(item.ExePath, item.Windows.FirstOrDefault());
            string id = CompatibilityAnalyzer.NormalizeCanonicalId(item.ExePath, rep);
            if (!windowByCanonical.TryGetValue(id, out var existing))
            {
                windowByCanonical[id] = item;
            }
            else
            {
                foreach (int p in item.Pids) existing.Pids.Add(p);
                foreach (IntPtr w in item.Windows) existing.Windows.Add(w);
            }
        }

        // Also build fast map of all running process names for tray-only apps
        var runningByProcName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName + ".exe";
                    if (!runningByProcName.TryGetValue(name, out var pidList))
                    {
                        pidList = [];
                        runningByProcName[name] = pidList;
                    }
                    pidList.Add(p.Id);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        foreach (DiscoveredApp app in list)
        {
            if (windowByCanonical.TryGetValue(app.Id, out RunningAppEntry? winEntry))
            {
                app.IsRunning = true;
                app.RunningProcessIds = [.. winEntry.Pids];
                app.MainWindowHandles = [.. winEntry.Windows];
            }
            else
            {
                string exeName = app.Compatibility.SquirrelProcessName ?? Path.GetFileName(app.ExecutablePath);
                if (!string.IsNullOrEmpty(exeName) &&
                    runningByProcName.TryGetValue(exeName, out List<int>? candidatePids))
                {
                    var verifiedPids = VerifyPidsMatchApp(app, candidatePids);
                    app.IsRunning = verifiedPids.Count > 0;
                    app.RunningProcessIds = verifiedPids;
                    app.MainWindowHandles = [];
                }
                else
                {
                    app.IsRunning = false;
                    app.RunningProcessIds = [];
                    app.MainWindowHandles = [];
                }
            }
        }
    }

    private static List<int> VerifyPidsMatchApp(DiscoveredApp app, List<int> candidatePids)
    {
        var matched = new List<int>();
        string expectedId = app.Id;
        foreach (int pid in candidatePids)
        {
            string? path = Win32Interop.TryGetProcessExecutablePath((uint)pid);
            if (string.IsNullOrEmpty(path))
                continue;

            if (app.Compatibility.Technology == ScalingTech.ElectronSquirrel &&
                !string.IsNullOrEmpty(app.Compatibility.SquirrelRootDir))
            {
                if (path.StartsWith(app.Compatibility.SquirrelRootDir, StringComparison.OrdinalIgnoreCase))
                    matched.Add(pid);
            }
            else if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(app.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(path.ToLowerInvariant(), expectedId, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(pid);
            }
        }
        return matched;
    }

    private void EnrichBackgroundProcessStatus(Dictionary<string, DiscoveredApp> byCanonicalId)
    {
        var runningByProcName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName + ".exe";
                    if (!runningByProcName.TryGetValue(name, out var pidList))
                    {
                        pidList = [];
                        runningByProcName[name] = pidList;
                    }
                    pidList.Add(p.Id);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        foreach (DiscoveredApp app in byCanonicalId.Values)
        {
            if (app.IsRunning)
                continue;

            string exeName = app.Compatibility.SquirrelProcessName ?? Path.GetFileName(app.ExecutablePath);
            if (!string.IsNullOrEmpty(exeName) &&
                runningByProcName.TryGetValue(exeName, out List<int>? candidatePids))
            {
                List<int> verified = VerifyPidsMatchApp(app, candidatePids);
                if (verified.Count > 0)
                {
                    app.IsRunning = true;
                    app.RunningProcessIds = verified;
                }
            }
        }
    }

    private void UpsertCandidate(
        Dictionary<string, DiscoveredApp> map,
        string exePath,
        string? preferredName,
        bool isInstalled,
        bool isRunning,
        IEnumerable<int>? pids,
        IEnumerable<IntPtr>? hwnds)
    {
        IntPtr sampleHwnd = hwnds?.FirstOrDefault() ?? IntPtr.Zero;
        CompatibilityReport report = _analyzer.Analyze(exePath, sampleHwnd);

        // Skip protected Windows internal system daemons from cluttering the app picker
        if (report.Technology == ScalingTech.UwpOrSystemProtected &&
            (exePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(report.TechnologyBadge, "DPIShift", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // If Squirrel Update.exe was discovered, normalize ExecutablePath to the latest inner app exe if available
        string displayExePath = exePath;
        if (report.Technology == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrEmpty(report.SquirrelRootDir) &&
            !string.IsNullOrEmpty(report.SquirrelProcessName))
        {
            string? innerExe = CompatibilityAnalyzer.ResolveLatestSquirrelExecutable(report.SquirrelRootDir, report.SquirrelProcessName);
            if (!string.IsNullOrEmpty(innerExe))
                displayExePath = innerExe;
        }

        string canonicalId = CompatibilityAnalyzer.NormalizeCanonicalId(displayExePath, report);

        if (map.TryGetValue(canonicalId, out DiscoveredApp? existing))
        {
            if (isInstalled)
                existing.IsInstalled = true;
            if (isRunning)
                existing.IsRunning = true;
            if (pids is not null)
            {
                foreach (int pid in pids)
                {
                    if (!existing.RunningProcessIds.Contains(pid))
                        existing.RunningProcessIds.Add(pid);
                }
            }
            if (hwnds is not null)
            {
                foreach (IntPtr h in hwnds)
                {
                    if (h != IntPtr.Zero && !existing.MainWindowHandles.Contains(h))
                        existing.MainWindowHandles.Add(h);
                }
            }
            if (sampleHwnd != IntPtr.Zero)
            {
                existing.Compatibility = report;
            }
            return;
        }

        (string friendlyName, string? publisher) = ExtractExecutableMetadata(displayExePath, preferredName, report);

        map[canonicalId] = new DiscoveredApp
        {
            Id = canonicalId,
            DisplayName = friendlyName,
            ExecutablePath = displayExePath,
            Publisher = publisher,
            IsInstalled = isInstalled,
            IsRunning = isRunning,
            RunningProcessIds = pids?.Distinct().ToList() ?? [],
            MainWindowHandles = hwnds?.Where(h => h != IntPtr.Zero).Distinct().ToList() ?? [],
            Compatibility = report
        };
    }

    private static (string Name, string? Publisher) ExtractExecutableMetadata(
        string exePath,
        string? preferredName,
        CompatibilityReport report)
    {
        string? publisher = null;
        string? description = null;
        string? productName = null;

        try
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
            publisher = string.IsNullOrWhiteSpace(fvi.CompanyName) ? null : fvi.CompanyName.Trim();
            description = string.IsNullOrWhiteSpace(fvi.FileDescription) ? null : fvi.FileDescription.Trim();
            productName = string.IsNullOrWhiteSpace(fvi.ProductName) ? null : fvi.ProductName.Trim();
        }
        catch
        {
            // Ignore
        }

        string chosenName = !string.IsNullOrWhiteSpace(preferredName)
            ? preferredName.Trim()
            : !string.IsNullOrWhiteSpace(report.JetBrainsProductName)
                ? report.JetBrainsProductName
                : !string.IsNullOrWhiteSpace(description) && !description.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? description
                    : !string.IsNullOrWhiteSpace(productName)
                        ? productName
                        : Path.GetFileNameWithoutExtension(exePath);

        return (chosenName, publisher);
    }

    private sealed class RunningAppEntry
    {
        public required string ExePath { get; init; }
        public HashSet<int> Pids { get; } = [];
        public List<IntPtr> Windows { get; } = [];
    }

    private static Dictionary<string, RunningAppEntry> CollectRunningWindowsSnapshot()
    {
        var map = new Dictionary<string, RunningAppEntry>(StringComparer.OrdinalIgnoreCase);
        int selfPid = Environment.ProcessId;

        Win32Interop.EnumWindows((hwnd, _) =>
        {
            if (!Win32Interop.IsTopLevelStandardWindow(hwnd))
                return true;

            string title = Win32Interop.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)selfPid)
                return true;

            string? exePath = Win32Interop.TryGetProcessExecutablePath(pid);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return true;

            if (!map.TryGetValue(exePath, out RunningAppEntry? entry))
            {
                entry = new RunningAppEntry { ExePath = exePath };
                map[exePath] = entry;
            }

            entry.Pids.Add((int)pid);
            entry.Windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return map;
    }

    private static IEnumerable<(string Path, bool Recurse)> GetShortcutSearchFolders()
    {
        (string Path, bool Recurse)[] candidates =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false)
        ];

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Path))
            .DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveEffectiveTargetExecutable(string targetPath, string arguments)
    {
        string expanded = Environment.ExpandEnvironmentVariables(targetPath.Trim('"'));

        // Handle Squirrel shortcuts: "...\Discord\Update.exe" --processStart Discord.exe
        if (Path.GetFileName(expanded).Equals("Update.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(arguments))
        {
            string? procName = TryExtractSquirrelProcessStartArg(arguments);
            string? rootDir = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrEmpty(procName) && !string.IsNullOrEmpty(rootDir))
            {
                string? inner = CompatibilityAnalyzer.ResolveLatestSquirrelExecutable(rootDir, procName);
                if (!string.IsNullOrEmpty(inner))
                    return inner;
            }
        }

        return expanded;
    }

    private static string ResolveProfileExecutablePath(AppScaleProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath) &&
            !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
        {
            string? root = Path.GetDirectoryName(profile.SquirrelUpdateExePath);
            if (!string.IsNullOrEmpty(root))
            {
                string? latest = CompatibilityAnalyzer.ResolveLatestSquirrelExecutable(root, profile.SquirrelProcessName);
                if (!string.IsNullOrEmpty(latest))
                    return latest;
            }
        }

        return profile.ExecutablePath;
    }

    internal static string? TryExtractSquirrelProcessStartArg(string arguments)
    {
        const string flag = "--processStart";
        int idx = arguments.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        string rest = arguments[(idx + flag.Length)..].TrimStart();
        if (rest.Length == 0)
            return null;

        if (rest[0] == '"')
        {
            int endQuote = rest.IndexOf('"', 1);
            return endQuote > 1 ? rest[1..endQuote] : null;
        }

        int space = rest.IndexOf(' ');
        string token = space > 0 ? rest[..space] : rest;
        return token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? token : token + ".exe";
    }

    private void DiscoverFromUninstallRegistry(Dictionary<string, DiscoveredApp> map, CancellationToken ct)
    {
        (RegistryKey Hive, string SubKey)[] roots =
        [
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        ];

        foreach ((RegistryKey hive, string subKeyPath) in roots)
        {
            ct.ThrowIfCancellationRequested();
            using RegistryKey? baseKey = hive.OpenSubKey(subKeyPath, writable: false);
            if (baseKey is null)
                continue;

            string[] subNames;
            try { subNames = baseKey.GetSubKeyNames(); }
            catch { continue; }

            foreach (string subName in subNames)
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey? appKey = baseKey.OpenSubKey(subName, writable: false);
                if (appKey is null)
                    continue;

                if (appKey.GetValue("SystemComponent") is int sysComp && sysComp == 1)
                    continue;

                string? displayName = appKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName) || IsNoiseShortcutTitle(displayName))
                    continue;

                string? exeCandidate = ExtractExeFromDisplayIcon(appKey.GetValue("DisplayIcon") as string);
                if (!IsValidCandidateExecutable(exeCandidate))
                    continue;

                UpsertCandidate(
                    map,
                    exeCandidate!,
                    preferredName: displayName.Trim(),
                    isInstalled: true,
                    isRunning: false,
                    pids: null,
                    hwnds: null);
            }
        }
    }

    private static string? ExtractExeFromDisplayIcon(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
            return null;

        string s = Environment.ExpandEnvironmentVariables(rawIcon.Trim());
        int commaIdx = s.LastIndexOf(',');
        if (commaIdx > 0)
            s = s[..commaIdx];

        s = s.Trim().Trim('"');
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    private static bool IsValidCandidateExecutable(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) ||
            !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(exePath))
        {
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(exePath);
        if (IgnoredNameKeywords.Any(k => fileName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (fileName.Equals("msiexec", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsNoiseShortcutTitle(string title) =>
        IgnoredNameKeywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string CleanShortcutTitle(string title) =>
        title.Replace(" - Shortcut", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace(" - Kısayol", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Trim();
}
