namespace DPIShift.Core.Models;

public enum ScalingTech
{
    Unknown = 0,
    Chromium,
    Electron,
    ElectronSquirrel,
    Cef,
    WebView2,
    JetBrainsIde,
    EclipseSwt,
    JavaDesktop,
    Qt6,
    Qt5,
    Gtk,
    Wpf,
    WinFormsPerMonitor,
    Win32PerMonitor,
    LegacySystemOrUnaware,
    UwpOrSystemProtected,
    GameOrAntiCheat
}

public enum SupportLevel
{
    Full = 0,
    Partial = 1,
    Unsupported = 2
}

public enum ScalingDeliveryMode
{
    None = 0,
    ChromiumFlags,
    WebView2Environment,
    JetBrainsVmOptions,
    EclipseIniOrArgs,
    JavaEnvironment,
    QtEnvironment,
    GtkEnvironment,
    LiveRuntimeDpiMessage
}
