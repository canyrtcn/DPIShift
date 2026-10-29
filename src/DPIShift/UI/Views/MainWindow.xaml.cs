using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DPIShift.Core.Native;
using DPIShift.UI.ViewModels;

namespace DPIShift.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Action _hideToTrayCallback;
    private readonly Action _explicitExitCallback;
    private bool _allowRealClose;

    public MainWindow(
        MainViewModel viewModel,
        Action hideToTrayCallback,
        Action explicitExitCallback)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hideToTrayCallback = hideToTrayCallback;
        _explicitExitCallback = explicitExitCallback;
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Activated += OnActivated;
    }

    public void ForceClose()
    {
        _allowRealClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.ApplyDarkTitleBar(hwnd);

        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == Win32Interop.WM_GETMINMAXINFO)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int minW = (int)Math.Round(MinWidth * (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0));
            int minH = (int)Math.Round(MinHeight * (dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0));
            Win32Interop.HandleMinMaxInfo(hwnd, lParam, minW, minH);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateCaptionGlyphs();
        await _viewModel.LoadApplicationsAsync();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _viewModel.RefreshLiveRunningStatus();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateCaptionGlyphs();

        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
        {
            HideToTray();
        }
    }

    private void UpdateCaptionGlyphs()
    {
        bool isMax = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        _hideToTrayCallback();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CompletelyExit_Click(object sender, RoutedEventArgs e)
    {
        _explicitExitCallback();
    }
}
