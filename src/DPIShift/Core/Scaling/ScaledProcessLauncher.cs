using System.Diagnostics;
using System.Globalization;
using DPIShift.Core.Detection;
using DPIShift.Core.Models;
using DPIShift.Core.Native;

namespace DPIShift.Core.Scaling;

public static class ScaledProcessLauncher
{
    public static async Task<int> LaunchScaledFromCliAsync(AppScaleProfile profile, string[]? extraArgs = null)
    {
        bool ok = LaunchScaledCore(profile, extraArgs, applyDefaultScale: false, fireAndForgetLiveWait: false, out bool needsLiveWait, out int scale);
        if (!ok)
            return 1;

        if (needsLiveWait)
            await WaitAndApplyLiveDpiAsync(profile.ExecutablePath, scale);

        return 0;
    }

    public static bool LaunchScaled(AppScaleProfile profile, string[]? extraArgs = null, bool applyDefaultScale = false) =>
        LaunchScaledCore(profile, extraArgs, applyDefaultScale, fireAndForgetLiveWait: true, out _, out _);

    private static bool LaunchScaledCore(
        AppScaleProfile profile,
        string[]? extraArgs,
        bool applyDefaultScale,
        bool fireAndForgetLiveWait,
        out bool needsLiveWait,
        out int effectiveScale)
    {
        needsLiveWait = false;
        effectiveScale = 100;

        string targetExe = ResolveLaunchTarget(profile, out bool useSquirrelUpdater);
        if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
            return false;

        int scale = applyDefaultScale || !profile.IsEnabled
            ? 100
            : ScaleMath.ClampPercent(profile.ScalePercent);
        effectiveScale = scale;

        string extraJoined = extraArgs is { Length: > 0 }
            ? string.Join(' ', extraArgs.Select(QuoteIfNeeded))
            : string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = targetExe,
            WorkingDirectory = Path.GetDirectoryName(targetExe) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        };

        if (scale != 100)
        {
            ConfigureScalingParameters(psi, profile, scale, useSquirrelUpdater, extraJoined);
        }
        else if (useSquirrelUpdater && !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
        {
            psi.Arguments = string.IsNullOrEmpty(extraJoined)
                ? $"--processStart {profile.SquirrelProcessName}"
                : $"--processStart {profile.SquirrelProcessName} --process-start-args \"{extraJoined}\"";
        }
        else
        {
            psi.Arguments = extraJoined;
        }

        try
        {
            Process? started = Process.Start(psi);
            if (started is null)
                return false;

            if (scale != 100 && profile.DeliveryMode == ScalingDeliveryMode.LiveRuntimeDpiMessage)
            {
                needsLiveWait = true;
                if (fireAndForgetLiveWait)
                {
                    _ = WaitAndApplyLiveDpiAsync(profile.ExecutablePath, scale);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RestartAndLaunchScaledAsync(AppScaleProfile profile)
    {
        await CloseRunningInstancesAsync(profile);
        await Task.Delay(250);
        return LaunchScaled(profile);
    }

    public static async Task<bool> RestartAtDefaultScaleAsync(AppScaleProfile profile)
    {
        await CloseRunningInstancesAsync(profile);
        await Task.Delay(250);
        return LaunchScaled(profile, extraArgs: null, applyDefaultScale: true);
    }

    public static async Task CloseRunningInstancesAsync(AppScaleProfile profile)
    {
        List<int> targetPids = FindMatchingProcessIds(profile);
        if (targetPids.Count == 0)
            return;

        var pidSet = targetPids.ToHashSet();

        // 1. Politely request top-level windows to close first so session data saves cleanly
        Win32Interop.EnumWindows((hwnd, _) =>
        {
            if (!Win32Interop.IsWindow(hwnd))
                return true;

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pidSet.Contains((int)pid) && Win32Interop.GetAncestor(hwnd, Win32Interop.GA_ROOT) == hwnd)
            {
                Win32Interop.PostMessageW(hwnd, Win32Interop.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);

        // 2. Wait up to 1.8s for graceful shutdown
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1800)
        {
            await Task.Delay(120);
            if (FindMatchingProcessIds(profile).Count == 0)
                return;
        }

        // 3. Cleanly terminate remaining background tray/helper processes so single-instance apps accept new launch flags
        foreach (int pid in FindMatchingProcessIds(profile))
        {
            try
            {
                using Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch
            {
                // Already exited
            }
        }

        // Brief wait for OS mutex/lock release
        sw.Restart();
        while (sw.ElapsedMilliseconds < 800 && FindMatchingProcessIds(profile).Count > 0)
        {
            await Task.Delay(100);
        }
    }

    public static List<int> FindMatchingProcessIds(AppScaleProfile profile)
    {
        var result = new List<int>();
        int selfPid = Environment.ProcessId;

        string? squirrelRoot = !string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath)
            ? Path.GetDirectoryName(profile.SquirrelUpdateExePath)
            : null;

        string? fullExe = null;
        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            try { fullExe = Path.GetFullPath(profile.ExecutablePath); } catch { }
        }

        string targetProcBase = !string.IsNullOrWhiteSpace(profile.SquirrelProcessName)
            ? Path.GetFileNameWithoutExtension(profile.SquirrelProcessName)
            : Path.GetFileNameWithoutExtension(profile.ExecutablePath);

        if (string.IsNullOrWhiteSpace(targetProcBase))
            return result;

        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(targetProcBase);
        }
        catch
        {
            return result;
        }

        foreach (Process p in candidates)
        {
            try
            {
                if (p.Id == selfPid)
                    continue;

                string? path = Win32Interop.TryGetProcessExecutablePath((uint)p.Id);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (!string.IsNullOrEmpty(squirrelRoot) &&
                    path.StartsWith(squirrelRoot, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(p.Id);
                }
                else if (!string.IsNullOrEmpty(fullExe) &&
                         string.Equals(Path.GetFullPath(path), fullExe, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(p.Id);
                }
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }

        return result;
    }

    public static async Task WaitAndApplyLiveDpiAsync(string executablePath, int scalePercent, int timeoutMs = 4500)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(180);
            int count = RuntimeDpiWatcher.ApplyLiveDpiToRunningExecutable(executablePath, scalePercent);
            if (count > 0)
            {
                // Apply one follow-up pass 350ms later in case splash screen transitioned to main window
                await Task.Delay(350);
                RuntimeDpiWatcher.ApplyLiveDpiToRunningExecutable(executablePath, scalePercent);
                return;
            }
        }
    }

    private static void ConfigureScalingParameters(
        ProcessStartInfo psi,
        AppScaleProfile profile,
        int scalePercent,
        bool useSquirrelUpdater,
        string extraJoined)
    {
        switch (profile.DeliveryMode)
        {
            case ScalingDeliveryMode.ChromiumFlags:
                if (useSquirrelUpdater && !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
                {
                    psi.Arguments = ScaleMath.InjectSquirrelProcessStartArgs(extraJoined, profile.SquirrelProcessName, scalePercent);
                }
                else
                {
                    psi.Arguments = ScaleMath.InjectChromiumFlags(extraJoined, scalePercent);
                }
                break;

            case ScalingDeliveryMode.WebView2Environment:
                psi.Arguments = extraJoined;
                psi.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = ScaleMath.BuildChromiumFlags(scalePercent);
                break;

            case ScalingDeliveryMode.QtEnvironment:
                psi.Arguments = extraJoined;
                int osScale = Win32Interop.GetPrimaryMonitorScalePercent();
                psi.Environment["QT_ENABLE_HIGHDPI_SCALING"] = "1";
                psi.Environment["QT_AUTO_SCREEN_SCALE_FACTOR"] = "1";
                psi.Environment["QT_SCALE_FACTOR_ROUNDING_POLICY"] = "PassThrough";
                psi.Environment["QT_SCALE_FACTOR"] = ScaleMath.FormatRelativeQtFactor(scalePercent, osScale);
                break;

            case ScalingDeliveryMode.GtkEnvironment:
                psi.Arguments = extraJoined;
                (string gdkScale, string gdkDpiScale) = ScaleMath.FormatGtkFactors(scalePercent);
                psi.Environment["GDK_SCALE"] = gdkScale;
                psi.Environment["GDK_DPI_SCALE"] = gdkDpiScale;
                break;

            case ScalingDeliveryMode.JavaEnvironment:
                psi.Arguments = extraJoined;
                string factor = ScaleMath.FormatAbsoluteFactorFromPercent(scalePercent);
                string scalePctStr = scalePercent.ToString(CultureInfo.InvariantCulture);
                string existingJavaOpts = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS") ?? string.Empty;
                psi.Environment["JAVA_TOOL_OPTIONS"] =
                    $"{existingJavaOpts} -Dsun.java2d.uiScale.enabled=true -Dsun.java2d.uiScale={factor} -Dglass.win.uiScale={scalePctStr}%".Trim();
                break;

            case ScalingDeliveryMode.EclipseIniOrArgs:
                string eclipseFlags = ScaleMath.BuildEclipseAppendVmArgs(scalePercent);
                psi.Arguments = string.IsNullOrEmpty(extraJoined) ? eclipseFlags : $"{extraJoined} {eclipseFlags}";
                break;

            case ScalingDeliveryMode.JetBrainsVmOptions:
            case ScalingDeliveryMode.LiveRuntimeDpiMessage:
            default:
                psi.Arguments = extraJoined;
                break;
        }
    }

    private static string ResolveLaunchTarget(AppScaleProfile profile, out bool useSquirrelUpdater)
    {
        useSquirrelUpdater = false;

        if (profile.DetectedTech == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrWhiteSpace(profile.SquirrelUpdateExePath) &&
            !string.IsNullOrWhiteSpace(profile.SquirrelProcessName))
        {
            if (File.Exists(profile.SquirrelUpdateExePath))
            {
                useSquirrelUpdater = true;
                return profile.SquirrelUpdateExePath;
            }

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

    private static string QuoteIfNeeded(string arg) =>
        arg.Contains(' ') && !arg.StartsWith('"') ? $"\"{arg}\"" : arg;
}
