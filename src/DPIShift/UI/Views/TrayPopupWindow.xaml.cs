using System.Windows;
using System.Windows.Media;
using DPIShift.Core.Models;
using DPIShift.Core.Native;
using DPIShift.Core.Scaling;

namespace DPIShift.UI.Views;

public partial class TrayPopupWindow : Window
{
    public sealed class QuickLaunchEntry
    {
        public required AppScaleProfile Profile { get; init; }
        public string DisplayName => Profile.DisplayName;
        public string ScaleLabel => $"%{Profile.ScalePercent}";
    }

    private readonly ScalingOrchestrator _orchestrator;
    private readonly Action _openMainAction;
    private readonly Action _toggleWatcherAction;
    private readonly Action _exitApplicationAction;

    public TrayPopupWindow(
        ScalingOrchestrator orchestrator,
        Action openMainAction,
        Action toggleWatcherAction,
        Action exitApplicationAction)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _openMainAction = openMainAction;
        _toggleWatcherAction = toggleWatcherAction;
        _exitApplicationAction = exitApplicationAction;

        PopulateState();
    }

    public void ShowNearCursor()
    {
        Win32Interop.GetCursorPos(out Win32Interop.POINT pt);

        // Position initially offscreen to measure exact rendered height, then place cleanly above taskbar cursor
        Left = -10000;
        Top = -10000;
        Show();
        UpdateLayout();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        double cursorDipX = pt.X / scaleX;
        double cursorDipY = pt.Y / scaleY;

        Rect workArea = SystemParameters.WorkArea;
        double targetLeft = Math.Clamp(cursorDipX - (ActualWidth / 2.0), workArea.Left + 8, workArea.Right - ActualWidth - 8);
        double targetTop = cursorDipY - ActualHeight - 6;
        if (targetTop < workArea.Top + 8)
            targetTop = Math.Min(cursorDipY + 6, workArea.Bottom - ActualHeight - 8);

        Left = targetLeft;
        Top = targetTop;
        Activate();
    }

    private void PopulateState()
    {
        int sysScale = Win32Interop.GetPrimaryMonitorScalePercent();
        SystemScaleText.Text = $" · Win %{sysScale}";

        List<AppScaleProfile> active = _orchestrator.Profiles
            .Where(p => p.IsEnabled && p.ScalePercent != 100)
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(7)
            .ToList();

        int totalActive = _orchestrator.Profiles.Count(p => p.IsEnabled && p.ScalePercent != 100);
        ActiveRulesText.Text = $"{totalActive} Aktif Kural";

        if (active.Count > 0)
        {
            QuickLaunchItems.ItemsSource = active.Select(p => new QuickLaunchEntry { Profile = p }).ToList();
            QuickLaunchSection.Visibility = Visibility.Visible;
        }
        else
        {
            QuickLaunchSection.Visibility = Visibility.Collapsed;
        }

        ToggleWatcherButton.Content = _orchestrator.Settings.EnableBackgroundRuntimeScaling
            ? "Canlı Pencere İzleyiciyi Duraklat"
            : "Canlı Pencere İzleyiciyi Etkinleştir";
    }

    private void QuickLaunchApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickLaunchEntry entry })
        {
            Close();
            ScaledProcessLauncher.LaunchScaled(entry.Profile);
        }
    }

    private void OpenMainWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _openMainAction();
    }

    private void ToggleWatcher_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _toggleWatcherAction();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _exitApplicationAction();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        try { Close(); } catch { }
    }
}
