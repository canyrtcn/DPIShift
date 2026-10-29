using System.IO.Pipes;
using System.Text;
using System.Windows;
using DPIShift.Core.Detection;
using DPIShift.Core.Localization;
using DPIShift.Core.Native;
using DPIShift.Core.Persistence;
using DPIShift.Core.Scaling;
using DPIShift.UI.ViewModels;
using DPIShift.UI.Views;

namespace DPIShift;

public partial class App : Application
{
    private readonly bool _startMinimized;
    private readonly string _ipcPipeName;
    private CancellationTokenSource? _ipcCts;

    private ConfigStore? _configStore;
    private CompatibilityAnalyzer? _analyzer;
    private AppDiscoveryService? _discoveryService;
    private ShortcutIntegrationService? _shortcutService;
    private RuntimeDpiWatcher? _runtimeWatcher;
    private ScalingOrchestrator? _orchestrator;
    private SystemTrayService? _trayService;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayPopupWindow? _activeTrayPopup;

    public App() : this(false, "DPIShift_WakePipe_Default")
    {
    }

    public App(bool startMinimized, string ipcPipeName)
    {
        _startMinimized = startMinimized;
        _ipcPipeName = ipcPipeName;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                Loc.Pick(
                    $"An unexpected error occurred:\n{args.Exception.Message}",
                    $"Beklenmeyen bir hata oluştu:\n{args.Exception.Message}"),
                "DPIShift",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        _configStore = new ConfigStore();
        _analyzer = new CompatibilityAnalyzer();
        _discoveryService = new AppDiscoveryService(_analyzer);
        _shortcutService = new ShortcutIntegrationService();
        _runtimeWatcher = new RuntimeDpiWatcher();
        _orchestrator = new ScalingOrchestrator(_configStore, _shortcutService, _runtimeWatcher);
        _orchestrator.InitializeRuntimeHooks();

        _trayService = new SystemTrayService();
        _trayService.OpenRequested += () => Dispatcher.InvokeAsync(ShowOrRestoreMainWindow);
        _trayService.ContextMenuRequested += () => Dispatcher.InvokeAsync(ShowTrayContextMenu);
        _trayService.Initialize();
        UpdateTrayTooltip();

        StartIpcWakeListener();

        if (!_startMinimized)
        {
            ShowOrRestoreMainWindow();
        }
        else
        {
            Win32Interop.TrimCurrentProcessWorkingSet();
        }
    }

    public void ShowOrRestoreMainWindow()
    {
        if (_orchestrator is null || _analyzer is null || _discoveryService is null)
            return;

        if (_mainViewModel is null)
        {
            _mainViewModel = new MainViewModel(_analyzer, _discoveryService, _orchestrator);
            _mainViewModel.ProfilesChanged += UpdateTrayTooltip;
        }

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(
                _mainViewModel,
                hideToTrayCallback: OnMainWindowHiddenToTray,
                explicitExitCallback: PerformCompleteExit);
        }

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void OnMainWindowHiddenToTray()
    {
        if (_orchestrator is not null && _orchestrator.Settings.ShowTrayNotificationOnFirstClose)
        {
            _orchestrator.Settings.ShowTrayNotificationOnFirstClose = false;
            _orchestrator.SaveSettings(_orchestrator.Settings);
            _trayService?.ShowBalloonNotification(
                Loc.Pick("DPIShift Running in Background", "DPIShift Arka Planda Hazır"),
                Loc.Pick(
                    "DPIShift minimized to the system tray. Click the tray icon to reopen, or right-click to exit.",
                    "DPIShift sistem tepsisine küçültüldü. Tekrar açmak için tepsi simgesine tıklayın, çıkmak için sağ tıklayın."));
        }

        // Release transient caches and trim OS working set while idle in tray
        IconExtractor.ClearCache();
        GC.Collect(1, GCCollectionMode.Optimized);
        Win32Interop.TrimCurrentProcessWorkingSet();
    }

    private void ShowTrayContextMenu()
    {
        if (_orchestrator is null)
            return;

        if (_activeTrayPopup is { IsVisible: true })
        {
            _activeTrayPopup.Close();
        }

        _activeTrayPopup = new TrayPopupWindow(
            _orchestrator,
            openMainAction: ShowOrRestoreMainWindow,
            toggleWatcherAction: ToggleRuntimeWatcherFromTray,
            exitApplicationAction: PerformCompleteExit);

        _activeTrayPopup.ShowNearCursor();
    }

    private void ToggleRuntimeWatcherFromTray()
    {
        if (_orchestrator is null)
            return;

        if (_mainViewModel is not null)
        {
            _mainViewModel.EnableBackgroundRuntimeScaling = !_mainViewModel.EnableBackgroundRuntimeScaling;
        }
        else
        {
            _orchestrator.Settings.EnableBackgroundRuntimeScaling = !_orchestrator.Settings.EnableBackgroundRuntimeScaling;
            _orchestrator.SaveSettings(_orchestrator.Settings);
            UpdateTrayTooltip();
        }
    }

    private void UpdateTrayTooltip()
    {
        if (_trayService is null || _orchestrator is null)
            return;

        int activeCount = _orchestrator.Profiles.Count(p => p.IsEnabled && p.ScalePercent != 100);
        string tip = activeCount > 0
            ? Loc.Pick(
                $"DPIShift — {activeCount} active scaling rule(s)",
                $"DPIShift — {activeCount} aktif uygulama ölçeği")
            : Loc.Pick(
                "DPIShift — Per-Application Display Scaling",
                "DPIShift — Uygulama Bazlı Ekran Ölçeklendirme");
        _trayService.UpdateTooltip(tip);
    }

    private void StartIpcWakeListener()
    {
        _ipcCts = new CancellationTokenSource();
        CancellationToken ct = _ipcCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _ipcPipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 2,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    byte[] buf = new byte[64];
                    int read = await server.ReadAsync(buf, ct);
                    string cmd = Encoding.UTF8.GetString(buf, 0, read).Trim();
                    if (cmd.Equals("SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = Dispatcher.InvokeAsync(ShowOrRestoreMainWindow);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    public void PerformCompleteExit()
    {
        _ipcCts?.Cancel();

        if (_activeTrayPopup is { IsVisible: true })
        {
            try { _activeTrayPopup.Close(); } catch { }
        }

        if (_mainWindow is not null)
        {
            try { _mainWindow.ForceClose(); } catch { }
            _mainWindow = null;
        }

        _runtimeWatcher?.Dispose();
        _runtimeWatcher = null;

        _trayService?.Dispose();
        _trayService = null;

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ipcCts?.Cancel();
        _runtimeWatcher?.Dispose();
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
