using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DPIShift.Core.Native;
using DPIShift.Core.Scaling;

namespace DPIShift.Tests;

public sealed class LiveRuntimeDpiIntegrationTests
{
    [Fact]
    public void LiveRuntimeDpiScaling_ScalesAndRestoresRealWpfWindow_WithoutAlteringGlobalWindowsScale()
    {
        int globalOsScaleBefore = Win32Interop.GetPrimaryMonitorScalePercent();
        Exception? threadError = null;
        double scaleAt150 = 0;
        double scaleAt125 = 0;
        double scaleAfterReset = 0;
        int widthBefore = 0;
        int widthAfterReset = 0;

        var staThread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Title = "DPIShift_IntegrationTestWindow",
                    Width = 400,
                    Height = 300,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -3000,
                    Top = -3000,
                    Content = new Button { Content = "Vector DPI Test", Width = 120, Height = 36 }
                };

                window.Show();
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                double baseOsScale = Win32Interop.GetMonitorDpiForWindow(hwnd) / 96.0;
                Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rcInitial);
                widthBefore = rcInitial.Width;

                // 1. Scale live window to 150%
                Assert.True(RuntimeDpiWatcher.SendDpiChangeToWindow(hwnd, 150));
                scaleAt150 = VisualTreeHelper.GetDpi(window).DpiScaleX;

                // 2. Transition live window to 125%
                Assert.True(RuntimeDpiWatcher.SendDpiChangeToWindow(hwnd, 125));
                scaleAt125 = VisualTreeHelper.GetDpi(window).DpiScaleX;

                // 3. Reset back to OS monitor scale
                int osMonitorPercent = (int)Math.Round(baseOsScale * 100.0);
                Assert.True(RuntimeDpiWatcher.SendDpiChangeToWindow(hwnd, osMonitorPercent));
                scaleAfterReset = VisualTreeHelper.GetDpi(window).DpiScaleX;

                Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rcRestored);
                widthAfterReset = rcRestored.Width;

                window.Close();
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        Assert.True(staThread.Join(TimeSpan.FromSeconds(10)));

        if (threadError is not null)
            throw threadError;

        Assert.Equal(1.50, scaleAt150, precision: 2);
        Assert.Equal(1.25, scaleAt125, precision: 2);
        Assert.Equal(globalOsScaleBefore / 100.0, scaleAfterReset, precision: 2);
        Assert.InRange(Math.Abs(widthAfterReset - widthBefore), 0, 2);

        // Verify Windows global display scaling was NEVER touched!
        int globalOsScaleAfter = Win32Interop.GetPrimaryMonitorScalePercent();
        Assert.Equal(globalOsScaleBefore, globalOsScaleAfter);
    }
}
