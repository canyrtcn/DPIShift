using DPIShift.Core.Models;
using DPIShift.Core.Native;

namespace DPIShift.Core.Scaling;

/// <summary>
/// Event-driven, zero-polling runtime DPI scaler using Win32 SetWinEventHook (WINEVENT_OUTOFCONTEXT).
/// Uses 0.00% CPU when idle and automatically unhooks when no live-runtime profiles are active.
/// </summary>
public sealed class RuntimeDpiWatcher : IDisposable
{
    private readonly object _lock = new();
    private readonly Win32Interop.WinEventDelegate _hookDelegate;
    private Dictionary<string, int> _activeScalePercentByExePath = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _showHookHandle;
    private IntPtr _moveSizeHookHandle;
    private bool _globallyEnabled = true;

    public RuntimeDpiWatcher()
    {
        // Keep delegate rooted on instance so GC never collects unmanaged callback pointer
        _hookDelegate = OnWinEvent;
    }

    public bool IsHookActive => _showHookHandle != IntPtr.Zero;

    public void UpdateActiveProfiles(IEnumerable<AppScaleProfile> profiles, bool globallyEnabled)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (globallyEnabled)
        {
            foreach (AppScaleProfile p in profiles)
            {
                if (!p.IsEnabled ||
                    !p.AutoApplyOnWindowOpen ||
                    p.DeliveryMode != ScalingDeliveryMode.LiveRuntimeDpiMessage ||
                    p.ScalePercent == 100 ||
                    string.IsNullOrWhiteSpace(p.ExecutablePath))
                {
                    continue;
                }

                try
                {
                    string normalizedPath = Path.GetFullPath(p.ExecutablePath);
                    map[normalizedPath] = ScaleMath.ClampPercent(p.ScalePercent);
                }
                catch
                {
                    // Skip invalid path
                }
            }
        }

        lock (_lock)
        {
            _globallyEnabled = globallyEnabled;
            _activeScalePercentByExePath = map;
        }

        // Must install/uninstall SetWinEventHook on UI thread (message pump thread)
        SyncHookState(shouldHook: globallyEnabled && map.Count > 0);
    }

    public static int ApplyLiveDpiToRunningExecutable(string executablePath, int targetScalePercent)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return 0;

        string fullTarget;
        try { fullTarget = Path.GetFullPath(executablePath); }
        catch { return 0; }

        int appliedWindows = 0;
        Win32Interop.EnumWindows((hwnd, _) =>
        {
            if (!Win32Interop.IsWindow(hwnd) || !Win32Interop.IsWindowVisible(hwnd))
                return true;

            if (Win32Interop.GetAncestor(hwnd, Win32Interop.GA_ROOT) != hwnd)
                return true;

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
                return true;

            string? procPath = Win32Interop.TryGetProcessExecutablePath(pid);
            if (string.IsNullOrEmpty(procPath) || !string.Equals(procPath, fullTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            if (SendDpiChangeToWindow(hwnd, targetScalePercent))
                appliedWindows++;

            return true;
        }, IntPtr.Zero);

        return appliedWindows;
    }

    public static int ResetLiveDpiForRunningExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return 0;

        string fullTarget;
        try { fullTarget = Path.GetFullPath(executablePath); }
        catch { return 0; }

        int resetWindows = 0;
        Win32Interop.EnumWindows((hwnd, _) =>
        {
            if (!Win32Interop.IsWindow(hwnd))
                return true;

            if (Win32Interop.GetAncestor(hwnd, Win32Interop.GA_ROOT) != hwnd)
                return true;

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
                return true;

            string? procPath = Win32Interop.TryGetProcessExecutablePath(pid);
            if (string.IsNullOrEmpty(procPath) || !string.Equals(procPath, fullTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            int monitorOsDpi = Win32Interop.GetMonitorDpiForWindow(hwnd);
            if (SendExactDpiToWindow(hwnd, monitorOsDpi, monitorOsDpi))
                resetWindows++;

            return true;
        }, IntPtr.Zero);

        return resetWindows;
    }

    public static bool SendDpiChangeToWindow(IntPtr hwnd, int targetScalePercent)
    {
        if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd))
            return false;

        int monitorOsDpi = Win32Interop.GetMonitorDpiForWindow(hwnd);
        int targetDpi = ScaleMath.PercentToWindowsDpi(targetScalePercent);
        return SendExactDpiToWindow(hwnd, targetDpi, monitorOsDpi);
    }

    private static bool SendExactDpiToWindow(IntPtr hwnd, int targetDpi, int monitorOsDpi)
    {
        if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd) || Win32Interop.IsIconic(hwnd))
            return false;

        int appliedProp = (int)Win32Interop.GetPropW(hwnd, Win32Interop.AppliedDpiPropName);
        int currentEffectiveDpi = appliedProp > 0 ? appliedProp : monitorOsDpi;
        if (currentEffectiveDpi <= 0)
            currentEffectiveDpi = 96;

        if (currentEffectiveDpi == targetDpi)
        {
            if (targetDpi == monitorOsDpi && appliedProp > 0)
                Win32Interop.RemovePropW(hwnd, Win32Interop.AppliedDpiPropName);
            return true;
        }

        if (!Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT currentRect))
            return false;

        Win32Interop.RECT suggestedRect;
        if (Win32Interop.IsZoomed(hwnd))
        {
            // Keep maximized window fitted to screen while updating its internal logical vector DPI
            suggestedRect = currentRect;
        }
        else
        {
            double ratio = (double)targetDpi / currentEffectiveDpi;
            suggestedRect = new Win32Interop.RECT
            {
                Left = currentRect.Left,
                Top = currentRect.Top,
                Right = currentRect.Left + Math.Max(120, (int)Math.Round(currentRect.Width * ratio)),
                Bottom = currentRect.Top + Math.Max(80, (int)Math.Round(currentRect.Height * ratio))
            };
        }

        IntPtr wParam = new((targetDpi << 16) | (targetDpi & 0xFFFF));
        IntPtr sent = Win32Interop.SendMessageTimeoutW(
            hwnd,
            Win32Interop.WM_DPICHANGED,
            wParam,
            ref suggestedRect,
            Win32Interop.SMTO_ABORTIFHUNG | Win32Interop.SMTO_NORMAL,
            1500,
            out _);

        if (sent == IntPtr.Zero)
            return false;

        if (targetDpi == monitorOsDpi)
            Win32Interop.RemovePropW(hwnd, Win32Interop.AppliedDpiPropName);
        else
            Win32Interop.SetPropW(hwnd, Win32Interop.AppliedDpiPropName, new IntPtr(targetDpi));

        return true;
    }

    private void SyncHookState(bool shouldHook)
    {
        if (shouldHook)
        {
            if (_showHookHandle == IntPtr.Zero)
            {
                _showHookHandle = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_OBJECT_SHOW,
                    Win32Interop.EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _hookDelegate,
                    0,
                    0,
                    Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);
            }

            if (_moveSizeHookHandle == IntPtr.Zero)
            {
                _moveSizeHookHandle = Win32Interop.SetWinEventHook(
                    Win32Interop.EVENT_SYSTEM_MOVESIZEEND,
                    Win32Interop.EVENT_SYSTEM_MOVESIZEEND,
                    IntPtr.Zero,
                    _hookDelegate,
                    0,
                    0,
                    Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);
            }
        }
        else
        {
            UninstallHooks();
        }
    }

    private void UninstallHooks()
    {
        if (_showHookHandle != IntPtr.Zero)
        {
            Win32Interop.UnhookWinEvent(_showHookHandle);
            _showHookHandle = IntPtr.Zero;
        }

        if (_moveSizeHookHandle != IntPtr.Zero)
        {
            Win32Interop.UnhookWinEvent(_moveSizeHookHandle);
            _moveSizeHookHandle = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        // Fast O(1) discard for non-window or child control events
        if (idObject != Win32Interop.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        Dictionary<string, int> snapshot;
        bool enabled;
        lock (_lock)
        {
            enabled = _globallyEnabled;
            snapshot = _activeScalePercentByExePath;
        }

        if (!enabled || snapshot.Count == 0)
            return;

        if (!Win32Interop.IsWindow(hwnd) || !Win32Interop.IsWindowVisible(hwnd))
            return;

        if (Win32Interop.GetAncestor(hwnd, Win32Interop.GA_ROOT) != hwnd)
            return;

        Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return;

        string? procPath = Win32Interop.TryGetProcessExecutablePath(pid);
        if (string.IsNullOrEmpty(procPath) || !snapshot.TryGetValue(procPath, out int scalePercent))
            return;

        if (eventType == Win32Interop.EVENT_SYSTEM_MOVESIZEEND)
        {
            // Re-assert DPI if moving across monitors triggered an OS WM_DPICHANGED reset
            Win32Interop.RemovePropW(hwnd, Win32Interop.AppliedDpiPropName);
        }

        SendDpiChangeToWindow(hwnd, scalePercent);
    }

    public void Dispose()
    {
        UninstallHooks();
    }
}
