using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DPIShift.Core.Models;
using DPIShift.Core.Native;

namespace DPIShift.Core.Detection;

public sealed class CompatibilityAnalyzer
{
    private readonly record struct CacheKey(string Path, long LastWriteTicks);
    private readonly ConcurrentDictionary<CacheKey, CompatibilityReport> _cache = new();

    private static readonly HashSet<string> ProtectedSystemExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "dwm.exe", "csrss.exe", "wininit.exe", "winlogon.exe",
        "lsass.exe", "services.exe", "svchost.exe", "smss.exe", "sihost.exe",
        "taskhostw.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe",
        "SearchHost.exe", "SearchApp.exe", "ApplicationFrameHost.exe", "Widgets.exe",
        "LockApp.exe", "SystemSettings.exe", "TextInputHost.exe", "ctfmon.exe",
        "RuntimeBroker.exe", "dllhost.exe", "conhost.exe", "OpenConsole.exe",
        "fontdrvhost.exe", "audiodg.exe", "spoolsv.exe"
    };

    private static readonly string[] ChromiumBrowserDlls =
    [
        "chrome.dll", "msedge.dll", "brave.dll", "vivaldi.dll",
        "opera_browser.dll", "opera.dll", "chromium.dll"
    ];

    public static string NormalizeCanonicalId(string exePath, CompatibilityReport? report = null)
    {
        if (report?.Technology == ScalingTech.ElectronSquirrel &&
            !string.IsNullOrWhiteSpace(report.SquirrelRootDir) &&
            !string.IsNullOrWhiteSpace(report.SquirrelProcessName))
        {
            return $"squirrel::{Path.GetFullPath(report.SquirrelRootDir).TrimEnd('\\').ToLowerInvariant()}::{report.SquirrelProcessName.ToLowerInvariant()}";
        }

        try
        {
            return Path.GetFullPath(exePath).ToLowerInvariant();
        }
        catch
        {
            return exePath.Trim().ToLowerInvariant();
        }
    }

    public CompatibilityReport Analyze(string exePath, IntPtr liveWindowHandle = default)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Unknown,
                TechnologyBadge = "Bulunamadı",
                Support = SupportLevel.Unsupported,
                SummaryTitle = "Uygulama yürütülebilir dosyası bulunamadı",
                Explanation = "Seçilen .exe dosyasına erişilemiyor veya dosya taşınmış."
            };
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(exePath);
        }
        catch
        {
            fullPath = exePath;
        }

        long ticks = 0;
        try { ticks = File.GetLastWriteTimeUtc(fullPath).Ticks; } catch { }

        var key = new CacheKey(fullPath.ToLowerInvariant(), ticks);
        if (liveWindowHandle == IntPtr.Zero && _cache.TryGetValue(key, out CompatibilityReport? cached))
            return cached;

        CompatibilityReport report = AnalyzeCore(fullPath, liveWindowHandle);
        _cache[key] = report;
        return report;
    }

    private static CompatibilityReport AnalyzeCore(string fullPath, IntPtr liveWindowHandle)
    {
        string fileName = Path.GetFileName(fullPath);
        string appDir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string? currentSelfPath = Environment.ProcessPath;

        // 0. Self check
        if (!string.IsNullOrWhiteSpace(currentSelfPath) &&
            string.Equals(fullPath, Path.GetFullPath(currentSelfPath), StringComparison.OrdinalIgnoreCase))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.UwpOrSystemProtected,
                TechnologyBadge = "DPIShift",
                Support = SupportLevel.Unsupported,
                DpiAwarenessLabel = "Per-Monitor V2",
                SummaryTitle = "DPIShift kendi arayüzünü harici kural ile ölçeklendirmez",
                Explanation = "DPIShift, diğer uygulamaların DPI ölçeklendirmesini yönetmek için tasarlanmıştır."
            };
        }

        // 1. Protected Windows Shell / UWP / WindowsApps check
        if (ProtectedSystemExecutables.Contains(fileName) ||
            fullPath.Contains(@"\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            bool isUwp = fullPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(fileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

            return new CompatibilityReport
            {
                Technology = ScalingTech.UwpOrSystemProtected,
                TechnologyBadge = isUwp ? "UWP / Store Paketi" : "Windows Sistem Bileşeni",
                Support = SupportLevel.Unsupported,
                DpiAwarenessLabel = "Sistem Korumalı",
                SummaryTitle = isUwp
                    ? "Korumalı UWP / Microsoft Store uygulaması"
                    : "Kritik Windows kabuk veya sistem işlemi",
                Explanation = isUwp
                    ? "Windows Store (AppContainer / CoreWindow) uygulamaları ölçeklendirmeyi doğrudan işletim sistemi kompozitöründen alır ve dışarıdan bağımsız DPI geçersiz kılmasına izin vermez."
                    : "Sistem kararlılığını korumak amacıyla Windows masaüstü kabuğu ve çekirdek sistem işlemlerine müdahale edilmez."
            };
        }

        // 2. Squirrel Electron Apps (e.g., Discord, Slack standalone, GitHub Desktop, Postman)
        CompatibilityReport? squirrelReport = TryDetectSquirrelElectron(fullPath, fileName, appDir);
        if (squirrelReport is not null)
            return squirrelReport;

        // 3. Game & Anti-Cheat Detection (protect gaming performance & anti-cheat integrity!)
        CompatibilityReport? gameReport = TryDetectGameOrAntiCheat(fullPath, appDir);
        if (gameReport is not null)
            return gameReport;

        // 4. JetBrains IDEs (DataGrip, IntelliJ IDEA, PyCharm, WebStorm, Rider, CLion, GoLand, Android Studio, etc.)
        CompatibilityReport? jetBrainsReport = TryDetectJetBrainsIde(fullPath, fileName, appDir);
        if (jetBrainsReport is not null)
            return jetBrainsReport;

        // 5. Standard Electron Applications (VS Code, Obsidian, Notion, Cursor, Figma, etc.)
        if (File.Exists(Path.Combine(appDir, "resources", "electron.asar")) ||
            (File.Exists(Path.Combine(appDir, "resources", "app.asar")) && HasChromiumPakFiles(appDir)) ||
            (Directory.Exists(Path.Combine(appDir, "resources", "app")) && HasChromiumPakFiles(appDir)))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Electron,
                TechnologyBadge = "Electron (Chromium)",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.ChromiumFlags,
                DpiAwarenessLabel = "Per-Monitor V2",
                SummaryTitle = "Gerçek vektörel arayüz ölçeklendirmesi tam destekleniyor",
                Explanation = "Electron tabanlı bu uygulama Chromium görüntüleme motorunu kullanır. Yalnızca içerik değil; kenar çubukları, sekmeler, menüler ve tüm arayüz bileşenleri bulanıklaşmadan hedef yüzdede vektörel olarak çizilir.",
                MechanismDescription = "Chromium cihaz ölçek çarpanı (--high-dpi-support=1 --force-device-scale-factor) ile başlatma ve kısayol entegrasyonu.",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = true
            };
        }

        // 6. Chromium Browsers (Google Chrome, Edge, Brave, Vivaldi, Opera)
        if (IsChromiumBrowserDirectory(appDir, fileName))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Chromium,
                TechnologyBadge = "Chromium Tarayıcı",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.ChromiumFlags,
                DpiAwarenessLabel = "Per-Monitor V2",
                SummaryTitle = "Gerçek tarayıcı arayüzü ve içerik ölçeklendirmesi tam destekleniyor",
                Explanation = "Sekme çubuğu, adres çubuğu, yer işaretleri, menüler, geliştirici araçları ve web sayfalarının tamamı gerçek HiDPI vektörel ölçekte çalışır (sayfa içi yakınlaştırmadan farklı olarak tüm tarayıcı arayüzünü ölçekler). Değişiklik için arka planda açık tüm örneklerinin kapatılıp yeniden başlatılması gerekir.",
                MechanismDescription = "Chromium yerel cihaz ölçeği (--high-dpi-support=1 --force-device-scale-factor) ve kısayol entegrasyonu.",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = true
            };
        }

        // 7. CEF (Chromium Embedded Framework — e.g., Spotify)
        if (File.Exists(Path.Combine(appDir, "libcef.dll")) ||
            File.Exists(Path.Combine(appDir, "bin", "cef", "cef.win7x64", "libcef.dll")))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Cef,
                TechnologyBadge = "CEF (Chromium Embedded)",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.ChromiumFlags,
                DpiAwarenessLabel = "Per-Monitor V2",
                SummaryTitle = "CEF tabanlı vektörel arayüz ölçeklendirmesi destekleniyor",
                Explanation = "Uygulama gömülü Chromium (CEF) arayüz motorunu kullanıyor. Tüm arayüz bileşenleri gerçek cihaz ölçek çarpanıyla keskin şekilde ölçeklendirilebilir.",
                MechanismDescription = "CEF cihaz ölçek çarpanı (--high-dpi-support=1 --force-device-scale-factor) ile başlatma.",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = true
            };
        }

        // 8. Eclipse RCP / SWT (e.g., DBeaver, Eclipse)
        string baseNameNoExt = Path.GetFileNameWithoutExtension(fullPath);
        string candidateIni = Path.Combine(appDir, baseNameNoExt + ".ini");
        if (File.Exists(candidateIni) && FileContainsText(candidateIni, "-vmargs"))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.EclipseSwt,
                TechnologyBadge = "Eclipse SWT / Java",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.EclipseIniOrArgs,
                DpiAwarenessLabel = "Per-Monitor HiDPI",
                SummaryTitle = "Eclipse / SWT vektörel arayüz ölçeklendirmesi destekleniyor",
                Explanation = "SWT ve Java2D ölçeklendirme parametreleri üzerinden menüler, simgeler, veri tabloları ve düzenleyiciler bağımsız yüzdede ölçeklendirilir.",
                MechanismDescription = "Equinox başlatıcı parametreleri (--launcher.appendVmargs -vmargs -Dswt.autoScale & -Dsun.java2d.uiScale).",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = true,
                EclipseIniPath = candidateIni
            };
        }

        // 9. Qt 6 / Qt 5 Applications (DLLs or static binary markers)
        BinaryInspectionResult binInfo = InspectExecutableAndManagedSibling(fullPath, appDir, baseNameNoExt);

        bool hasQt6 = File.Exists(Path.Combine(appDir, "Qt6Core.dll")) ||
                      File.Exists(Path.Combine(appDir, "Qt6Widgets.dll")) ||
                      binInfo.HasQt6Marker;
        bool hasQt5 = !hasQt6 && (File.Exists(Path.Combine(appDir, "Qt5Core.dll")) ||
                                  File.Exists(Path.Combine(appDir, "Qt5Widgets.dll")) ||
                                  binInfo.HasQtScaleMarker);

        if (hasQt6 || hasQt5)
        {
            return new CompatibilityReport
            {
                Technology = hasQt6 ? ScalingTech.Qt6 : ScalingTech.Qt5,
                TechnologyBadge = hasQt6 ? "Qt 6" : "Qt 5",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.QtEnvironment,
                DpiAwarenessLabel = "Per-Monitor HiDPI",
                SummaryTitle = $"{(hasQt6 ? "Qt 6" : "Qt 5")} yerel kesirli HiDPI ölçeklendirmesi destekleniyor",
                Explanation = "Qt arayüz motoru bağımsız ölçek çarpanını ve kesirli (ör. %115, %125, %150) yuvarlamasız ölçeklendirmeyi doğrudan destekler. Sistem genelindeki diğer Qt uygulamalarını etkilememek için ayar yalnızca bu uygulamanın çalışma ortamına uygulanır.",
                MechanismDescription = "Süreç bazlı QT_SCALE_FACTOR (Windows ekran ölçeğine göre otomatik oranlanır) ve QT_SCALE_FACTOR_ROUNDING_POLICY=PassThrough.",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = false
            };
        }

        // 10. GTK 3 / GTK 4 Applications (GIMP, Inkscape, Meld, etc.)
        if (HasGtkLibraries(appDir) || binInfo.HasGtkMarker)
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Gtk,
                TechnologyBadge = "GTK 3 / GTK 4",
                Support = SupportLevel.Partial,
                DeliveryMode = ScalingDeliveryMode.GtkEnvironment,
                DpiAwarenessLabel = "GTK HiDPI",
                SummaryTitle = "GTK arayüz ve metin ölçeklendirmesi kısmen destekleniyor",
                Explanation = "GTK araç takımı Windows üzerinde metin ve simge boyutlarını GDK_DPI_SCALE ile kesirli oranlarda ölçekleyebilir; ancak bazı sabit piksel kenar boşlukları yalnızca tam sayı katlarında (%100, %200) tam genişler.",
                MechanismDescription = "Süreç bazlı GDK_SCALE ve GDK_DPI_SCALE ortam değişkenleri ile DPIShift başlatıcısı üzerinden çalıştırma.",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = false
            };
        }

        // 11. General Java Desktop (Swing / AWT / JavaFX / jpackage / Launch4j)
        if (IsJavaDesktopApplication(appDir, fileName))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.JavaDesktop,
                TechnologyBadge = "Java (Swing / JavaFX)",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.JavaEnvironment,
                DpiAwarenessLabel = "Java2D HiDPI",
                SummaryTitle = "Java masaüstü HiDPI arayüz ölçeklendirmesi destekleniyor",
                Explanation = "Java 9+ Java2D ve JavaFX görüntüleme hattı bağımsız arayüz ölçek çarpanını destekler. Yalnızca hedef Java sürecine özel ölçek parametresi aktarılır.",
                MechanismDescription = "Süreç bazlı JAVA_TOOL_OPTIONS (-Dsun.java2d.uiScale ve -Dglass.win.uiScale).",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = false
            };
        }

        // 12. WebView2 / Tauri Applications (non-WPF/WinForms hosts embedding WebView2, e.g. Tauri / Rust / C++ WebView2)
        bool hasWebView2 = File.Exists(Path.Combine(appDir, "WebView2Loader.dll")) || binInfo.HasWebView2Marker;
        if (hasWebView2 && !binInfo.IsWpf && !binInfo.IsWinForms)
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.WebView2,
                TechnologyBadge = "WebView2 / Tauri",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.WebView2Environment,
                DpiAwarenessLabel = "Per-Monitor V2",
                SummaryTitle = "WebView2 tabanlı vektörel arayüz ölçeklendirmesi destekleniyor",
                Explanation = "Microsoft Edge WebView2 çalışma zamanını kullanan bu uygulamanın tüm web tabanlı arayüzü bağımsız cihaz ölçek çarpanıyla keskin biçimde ölçeklendirilir.",
                MechanismDescription = "Süreç bazlı WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS (--force-device-scale-factor).",
                RequiresRestart = true,
                SupportsDirectShortcutFlags = false
            };
        }

        // Determine effective DPI awareness from live window (most accurate) or embedded PE manifest
        DpiAwarenessKind awareness = GetEffectiveDpiAwareness(liveWindowHandle, binInfo.ManifestAwareness);
        string awarenessLabel = awareness switch
        {
            DpiAwarenessKind.PerMonitorV2 => "Per-Monitor V2",
            DpiAwarenessKind.PerMonitor => "Per-Monitor",
            DpiAwarenessKind.System => "System DPI",
            _ => "DPI Unaware / Eski"
        };

        // 13. .NET WPF Applications (Live Runtime Vector DPI Scaling!)
        if (binInfo.IsWpf)
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Wpf,
                TechnologyBadge = ".NET WPF",
                Support = SupportLevel.Full,
                DeliveryMode = ScalingDeliveryMode.LiveRuntimeDpiMessage,
                DpiAwarenessLabel = awareness is DpiAwarenessKind.Unknown ? "WPF Vektörel DIP" : awarenessLabel,
                SummaryTitle = "Canlı çalışma zamanı vektörel DPI ölçeklendirmesi tam destekleniyor",
                Explanation = "WPF, cihazdan bağımsız vektörel birimlerle (DIP) çizim yapar. Uygulamayı yeniden başlatmaya gerek kalmadan açık pencereye anında gerçek vektörel DPI ölçeği uygulanabilir ve tek tıkla %100'e geri döndürülebilir.",
                MechanismDescription = "Canlı Win32 WM_DPICHANGED vektörel DPI sinyali ve pencere açılışında sıfır-CPU olay tabanlı otomatik uygulama.",
                RequiresRestart = false,
                SupportsDirectShortcutFlags = false
            };
        }

        // 14. .NET WinForms Applications
        if (binInfo.IsWinForms)
        {
            if (awareness is DpiAwarenessKind.PerMonitorV2 or DpiAwarenessKind.PerMonitor)
            {
                return new CompatibilityReport
                {
                    Technology = ScalingTech.WinFormsPerMonitor,
                    TechnologyBadge = ".NET WinForms (PMv2)",
                    Support = SupportLevel.Full,
                    DeliveryMode = ScalingDeliveryMode.LiveRuntimeDpiMessage,
                    DpiAwarenessLabel = awarenessLabel,
                    SummaryTitle = "Per-Monitor DPI uyumlu WinForms canlı ölçeklendirme destekleniyor",
                    Explanation = "Bu modern WinForms uygulaması Per-Monitor DPI desteğine sahiptir. Yeniden başlatma gerekmeden pencere kontrolleri ve yazı tipleri canlı olarak hedef DPI oranına ölçeklendirilebilir.",
                    MechanismDescription = "Canlı Win32 WM_DPICHANGED DPI sinyali ve pencere açılışında otomatik uygulama.",
                    RequiresRestart = false
                };
            }

            return new CompatibilityReport
            {
                Technology = ScalingTech.LegacySystemOrUnaware,
                TechnologyBadge = ".NET WinForms (Klasik)",
                Support = SupportLevel.Unsupported,
                DeliveryMode = ScalingDeliveryMode.None,
                DpiAwarenessLabel = awarenessLabel,
                SummaryTitle = "Bağımsız vektörel DPI ölçeklendirmesi desteklenmiyor",
                Explanation = "Bu klasik WinForms uygulaması Per-Monitor V2 DPI bildirimi içermiyor ve yalnızca Windows açılışındaki genel sistem DPI değerine kilitleniyor. Bulanık bitmap büyütme kullanmadan sistem ölçeğinden bağımsız keskin arayüz ölçeklendirmesi teknik olarak mümkün değildir."
            };
        }

        // 15. Native Win32 / C++ / WinUI Applications with Per-Monitor DPI Awareness
        if (awareness is DpiAwarenessKind.PerMonitorV2 or DpiAwarenessKind.PerMonitor)
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.Win32PerMonitor,
                TechnologyBadge = binInfo.IsWinUi ? "WinUI 3" : "Yerel Win32 (Per-Monitor)",
                Support = SupportLevel.Partial,
                DeliveryMode = ScalingDeliveryMode.LiveRuntimeDpiMessage,
                DpiAwarenessLabel = awarenessLabel,
                SummaryTitle = "Yerel Per-Monitor DPI sinyali kısmen destekleniyor",
                Explanation = "Uygulama Per-Monitor DPI farkındalığına sahip. DPIShift pencereye canlı WM_DPICHANGED DPI sinyali gönderebilir; WM_DPICHANGED parametresini kullanan bileşenler keskin biçimde ölçeklenir, ancak donanım monitör DPI'ını (GetDpiForWindow) her çizimde yeniden sorgulayan yerel kontroller varsayılan boyutta kalabilir.",
                MechanismDescription = "Canlı Win32 WM_DPICHANGED DPI sinyali (DLL enjeksiyonu olmadan güvenli mod).",
                RequiresRestart = false
            };
        }

        // 16. Legacy Win32 / GDI / System-DPI-Locked or DPI-Unaware apps
        return new CompatibilityReport
        {
            Technology = ScalingTech.LegacySystemOrUnaware,
            TechnologyBadge = "Klasik Win32 / GDI",
            Support = SupportLevel.Unsupported,
            DeliveryMode = ScalingDeliveryMode.None,
            DpiAwarenessLabel = awarenessLabel,
            SummaryTitle = "Gerçek bağımsız DPI ölçeklendirmesi desteklenmiyor",
            Explanation = "Bu uygulama sabit piksel veya yalnızca genel Windows Sistem DPI modunu kullanıyor. Windows genel ölçeği değiştirilmeden bu uygulamayı büyütmek yalnızca görüntüyü bulanıklaştıran bitmap esnetme ile yapılabilir; DPIShift bulanık ölçeklendirme uygulamak yerine uygulamayı korur."
        };
    }

    public static string? ResolveLatestSquirrelExecutable(string squirrelRootDir, string processName)
    {
        if (!Directory.Exists(squirrelRootDir))
            return null;

        try
        {
            var candidates = Directory.EnumerateDirectories(squirrelRootDir, "app-*")
                .Select(dir => new
                {
                    Dir = dir,
                    Exe = Path.Combine(dir, processName),
                    Ver = TryParseAppFolderVersion(Path.GetFileName(dir))
                })
                .Where(x => File.Exists(x.Exe))
                .OrderByDescending(x => x.Ver)
                .ThenByDescending(x => x.Dir, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return candidates?.Exe;
        }
        catch
        {
            return null;
        }
    }

    private static Version TryParseAppFolderVersion(string folderName)
    {
        if (folderName.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(folderName[4..].Split('-')[0], out Version? v))
        {
            return v;
        }

        return new Version(0, 0);
    }

    private static CompatibilityReport? TryDetectSquirrelElectron(string fullPath, string fileName, string appDir)
    {
        // Case A: Selected Update.exe directly in Squirrel root
        if (string.Equals(fileName, "Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? latestAppDir = Directory.EnumerateDirectories(appDir, "app-*")
                    .OrderByDescending(d => TryParseAppFolderVersion(Path.GetFileName(d)))
                    .FirstOrDefault();

                if (latestAppDir is not null &&
                    (File.Exists(Path.Combine(latestAppDir, "resources", "app.asar")) ||
                     File.Exists(Path.Combine(latestAppDir, "resources", "electron.asar")) ||
                     HasChromiumPakFiles(latestAppDir)))
                {
                    string folderTitle = Path.GetFileName(appDir);
                    string candidateExe = Path.Combine(latestAppDir, folderTitle + ".exe");
                    if (!File.Exists(candidateExe))
                    {
                        candidateExe = Directory.EnumerateFiles(latestAppDir, "*.exe")
                            .FirstOrDefault(e => !Path.GetFileName(e).Contains("quirrel", StringComparison.OrdinalIgnoreCase) &&
                                                 !Path.GetFileName(e).Contains("crashpad", StringComparison.OrdinalIgnoreCase))
                            ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(candidateExe))
                    {
                        string innerProcName = Path.GetFileName(candidateExe);
                        return BuildSquirrelReport(appDir, fullPath, innerProcName);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        // Case B: Selected <Root>\app-1.0.xxxx\<App>.exe (such as running Discord.exe!)
        string dirName = Path.GetFileName(appDir);
        string? parentDir = Path.GetDirectoryName(appDir);
        if (dirName.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(parentDir))
        {
            string updateExe = Path.Combine(parentDir, "Update.exe");
            if (File.Exists(updateExe) &&
                (File.Exists(Path.Combine(appDir, "resources", "app.asar")) ||
                 File.Exists(Path.Combine(appDir, "resources", "electron.asar")) ||
                 HasChromiumPakFiles(appDir)))
            {
                return BuildSquirrelReport(parentDir, updateExe, fileName);
            }
        }

        return null;
    }

    private static CompatibilityReport BuildSquirrelReport(string rootDir, string updateExe, string innerProcessName) =>
        new()
        {
            Technology = ScalingTech.ElectronSquirrel,
            TechnologyBadge = "Electron (Squirrel)",
            Support = SupportLevel.Full,
            DeliveryMode = ScalingDeliveryMode.ChromiumFlags,
            DpiAwarenessLabel = "Per-Monitor V2",
            SummaryTitle = "Gerçek vektörel arayüz ölçeklendirmesi tam destekleniyor",
            Explanation = $"{Path.GetFileNameWithoutExtension(innerProcessName)} Electron (Chromium) arayüz motorunu ve Squirrel güncelleyicisini kullanıyor. Sunucu/kanal listesi, sohbet alanı, menüler ve tüm arayüz bileşenleri otomatik güncellemelerden etkilenmeden hedef yüzdede keskin biçimde ölçeklendirilir.",
            MechanismDescription = $"Squirrel başlatıcı köprüsü (Update.exe --processStart {innerProcessName} --process-start-args \"--force-device-scale-factor=...\") ve kısayol/başlangıç entegrasyonu.",
            RequiresRestart = true,
            SupportsDirectShortcutFlags = true,
            SquirrelRootDir = rootDir,
            SquirrelUpdateExePath = updateExe,
            SquirrelProcessName = innerProcessName
        };

    private static CompatibilityReport? TryDetectGameOrAntiCheat(string fullPath, string appDir)
    {
        if (File.Exists(Path.Combine(appDir, "UnityPlayer.dll")) ||
            File.Exists(Path.Combine(appDir, "EasyAntiCheat_EOS_Setup.exe")) ||
            Directory.Exists(Path.Combine(appDir, "EasyAntiCheat")) ||
            Directory.Exists(Path.Combine(appDir, "BattlEye")) ||
            File.Exists(Path.Combine(appDir, "nvngx_dlss.dll")) ||
            fullPath.Contains(@"\Engine\Binaries\Win64\", StringComparison.OrdinalIgnoreCase) ||
            (File.Exists(Path.Combine(appDir, "steam_api64.dll")) && !File.Exists(Path.Combine(appDir, "libcef.dll"))))
        {
            return new CompatibilityReport
            {
                Technology = ScalingTech.GameOrAntiCheat,
                TechnologyBadge = "Oyun / 3D Grafik Motoru",
                Support = SupportLevel.Unsupported,
                DeliveryMode = ScalingDeliveryMode.None,
                DpiAwarenessLabel = "DirectX / Vulkan",
                SummaryTitle = "Oyun ve tam ekran 3D grafik motoru (Korumalı)",
                Explanation = "3D oyunlar kendi iç çözünürlük ve HUD ölçeklendirme sistemlerini kullanır. Oyun performansını etkilememek ve hile koruma (Anti-Cheat) sistemleriyle tam uyumluluğu korumak için DPIShift oyun süreçlerine kesinlikle dokunmaz."
            };
        }

        return null;
    }

    private static CompatibilityReport? TryDetectJetBrainsIde(string fullPath, string fileName, string appDir)
    {
        // JetBrains executables normally reside in <IdeRoot>\bin\<product>64.exe
        string? installRoot = null;
        if (File.Exists(Path.Combine(appDir, "product-info.json")) || Directory.Exists(Path.Combine(appDir, "jbr")))
        {
            installRoot = appDir;
        }
        else
        {
            string? parent = Path.GetDirectoryName(appDir);
            if (!string.IsNullOrEmpty(parent) &&
                (File.Exists(Path.Combine(parent, "product-info.json")) || Directory.Exists(Path.Combine(parent, "jbr"))))
            {
                installRoot = parent;
            }
        }

        if (installRoot is null)
            return null;

        string productName = Path.GetFileNameWithoutExtension(fileName).Replace("64", string.Empty, StringComparison.OrdinalIgnoreCase);
        string vendorFolder = "JetBrains";
        string? dataDirName = null;

        string productInfoPath = Path.Combine(installRoot, "product-info.json");
        if (File.Exists(productInfoPath))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(productInfoPath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("name", out JsonElement nameEl) && nameEl.GetString() is { Length: > 0 } n)
                    productName = n;
                if (root.TryGetProperty("productVendor", out JsonElement vendorEl) && vendorEl.GetString() is { Length: > 0 } v)
                    vendorFolder = v;
                if (root.TryGetProperty("dataDirectoryName", out JsonElement dataEl) && dataEl.GetString() is { Length: > 0 } d)
                    dataDirName = d;
            }
            catch
            {
                // Fallback below
            }
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string? vmOptionsPath = null;

        if (!string.IsNullOrWhiteSpace(dataDirName))
        {
            // Android Studio uses Google\<dataDirectoryName>
            string vendorRoot = vendorFolder.Contains("Google", StringComparison.OrdinalIgnoreCase) ? "Google" : "JetBrains";
            vmOptionsPath = Path.Combine(appData, vendorRoot, dataDirName, fileName + ".vmoptions");
        }
        else
        {
            string jbRoot = Path.Combine(appData, "JetBrains");
            if (Directory.Exists(jbRoot))
            {
                string prefix = Path.GetFileNameWithoutExtension(fileName).Replace("64", string.Empty, StringComparison.OrdinalIgnoreCase);
                string? existingDir = Directory.EnumerateDirectories(jbRoot)
                    .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (existingDir is not null)
                    vmOptionsPath = Path.Combine(existingDir, fileName + ".vmoptions");
            }
        }

        return new CompatibilityReport
        {
            Technology = ScalingTech.JetBrainsIde,
            TechnologyBadge = "JetBrains Runtime (JBR)",
            Support = SupportLevel.Full,
            DeliveryMode = ScalingDeliveryMode.JetBrainsVmOptions,
            DpiAwarenessLabel = "JBR Per-Monitor HiDPI",
            SummaryTitle = $"{productName} için gerçek vektörel HiDPI ölçeklendirmesi tam destekleniyor",
            Explanation = "JetBrains Runtime (JBR), tüm SVG simgeleri, proje ağacı, veri tabloları, araç çubukları, menüler ve kod düzenleyiciyi bağımsız HiDPI ölçeğinde vektörel olarak çizer. Ayar kullanıcı profilindeki .vmoptions dosyasına kaydedildiği için Başlat Menüsü, Görev Çubuğu veya JetBrains Toolbox'tan açıldığında da otomatik çalışır.",
            MechanismDescription = vmOptionsPath is not null
                ? $"Kullanıcı VM seçenekleri ({Path.GetFileName(vmOptionsPath)}: -Dsun.java2d.uiScale) ve süreç başlatıcı desteği."
                : "JetBrains Runtime VM ölçek parametresi (-Dsun.java2d.uiScale).",
            RequiresRestart = true,
            SupportsDirectShortcutFlags = false,
            SupportsPersistentConfigFile = vmOptionsPath is not null,
            JetBrainsVmOptionsPath = vmOptionsPath,
            JetBrainsProductName = productName
        };
    }

    private static bool HasChromiumPakFiles(string dir) =>
        File.Exists(Path.Combine(dir, "chrome_100_percent.pak")) ||
        File.Exists(Path.Combine(dir, "icudtl.dat")) ||
        File.Exists(Path.Combine(dir, "v8_context_snapshot.bin"));

    private static bool IsChromiumBrowserDirectory(string appDir, string fileName)
    {
        if (HasChromiumPakFiles(appDir) && ChromiumBrowserDlls.Any(dll => File.Exists(Path.Combine(appDir, dll))))
            return true;

        if (File.Exists(Path.Combine(appDir, "chrome_elf.dll")) ||
            File.Exists(Path.Combine(appDir, "msedge_elf.dll")) ||
            File.Exists(Path.Combine(appDir, "opera_autoupdate.exe")))
            return true;

        // Chrome/Edge/Brave/Vivaldi place chrome.dll inside <AppDir>\<Version>\chrome.dll
        try
        {
            foreach (string subDir in Directory.EnumerateDirectories(appDir))
            {
                string subName = Path.GetFileName(subDir);
                if (subName.Length > 0 && char.IsDigit(subName[0]))
                {
                    if (HasChromiumPakFiles(subDir) || ChromiumBrowserDlls.Any(dll => File.Exists(Path.Combine(subDir, dll))))
                        return true;
                }
            }
        }
        catch
        {
            // Ignore permission issues
        }

        return fileName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("brave.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("vivaldi.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGtkLibraries(string appDir)
    {
        string[] probeDirs = [appDir, Path.Combine(appDir, "bin")];
        foreach (string dir in probeDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                if (Directory.EnumerateFiles(dir, "*gtk-*.dll").Any() ||
                    Directory.EnumerateFiles(dir, "libgdk-*.dll").Any())
                    return true;
            }
            catch
            {
                // Ignore
            }
        }

        return false;
    }

    private static bool IsJavaDesktopApplication(string appDir, string fileName)
    {
        if (fileName.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("java.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Directory.Exists(Path.Combine(appDir, "jre")) ||
            Directory.Exists(Path.Combine(appDir, "runtime")) ||
            File.Exists(Path.Combine(appDir, "jvm.dll")))
            return true;

        string appCfgDir = Path.Combine(appDir, "app");
        if (Directory.Exists(appCfgDir))
        {
            try
            {
                if (Directory.EnumerateFiles(appCfgDir, "*.cfg").Any())
                    return true;
            }
            catch { }
        }

        return false;
    }

    private static bool FileContainsText(string path, string needle)
    {
        try
        {
            using var sr = new StreamReader(path);
            char[] buf = new char[16384];
            int read = sr.Read(buf, 0, buf.Length);
            return new string(buf, 0, read).Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private enum DpiAwarenessKind
    {
        Unknown = 0,
        Unaware,
        System,
        PerMonitor,
        PerMonitorV2
    }

    private sealed class BinaryInspectionResult
    {
        public bool IsWpf { get; set; }
        public bool IsWinForms { get; set; }
        public bool IsWinUi { get; set; }
        public bool HasQt6Marker { get; set; }
        public bool HasQtScaleMarker { get; set; }
        public bool HasGtkMarker { get; set; }
        public bool HasWebView2Marker { get; set; }
        public DpiAwarenessKind ManifestAwareness { get; set; } = DpiAwarenessKind.Unknown;
    }

    private static DpiAwarenessKind GetEffectiveDpiAwareness(IntPtr liveWindowHandle, DpiAwarenessKind manifestAwareness)
    {
        if (liveWindowHandle != IntPtr.Zero && Win32Interop.IsWindow(liveWindowHandle))
        {
            try
            {
                IntPtr ctx = Win32Interop.GetWindowDpiAwarenessContext(liveWindowHandle);
                if (ctx != IntPtr.Zero)
                {
                    int val = Win32Interop.GetAwarenessFromDpiAwarenessContext(ctx);
                    return val switch
                    {
                        2 => manifestAwareness == DpiAwarenessKind.PerMonitorV2 ? DpiAwarenessKind.PerMonitorV2 : DpiAwarenessKind.PerMonitor,
                        1 => DpiAwarenessKind.System,
                        0 => DpiAwarenessKind.Unaware,
                        _ => manifestAwareness
                    };
                }
            }
            catch
            {
                // Fallback to manifest
            }
        }

        return manifestAwareness;
    }

    private static BinaryInspectionResult InspectExecutableAndManagedSibling(string exePath, string appDir, string baseNameNoExt)
    {
        var result = new BinaryInspectionResult();

        // Check side-by-side .manifest file if present
        string externalManifest = exePath + ".manifest";
        if (File.Exists(externalManifest))
        {
            try
            {
                result.ManifestAwareness = ParseManifestDpiAwareness(File.ReadAllText(externalManifest));
            }
            catch { }
        }

        // Check sibling DLL markers in directory (fast O(1) File.Exists checks)
        if (File.Exists(Path.Combine(appDir, "PresentationFramework.dll")) ||
            File.Exists(Path.Combine(appDir, "wpfgfx_cor3.dll")) ||
            File.Exists(Path.Combine(appDir, "PresentationNative_cor3.dll")))
        {
            result.IsWpf = true;
        }

        if (File.Exists(Path.Combine(appDir, "Microsoft.ui.xaml.dll")) ||
            File.Exists(Path.Combine(appDir, "Microsoft.WindowsAppRuntime.dll")))
        {
            result.IsWinUi = true;
        }

        // Scan main .exe (first 2 MB + last 1 MB covers PE headers, import tables, .rsrc manifest, and .NET metadata)
        ScanBinaryMarkers(exePath, result, scanManifest: result.ManifestAwareness == DpiAwarenessKind.Unknown);

        // For .NET Core / .NET 5+ apphost executables, managed assembly metadata is in <BaseName>.dll next to <BaseName>.exe!
        string managedSiblingDll = Path.Combine(appDir, baseNameNoExt + ".dll");
        if (File.Exists(managedSiblingDll))
        {
            ScanBinaryMarkers(managedSiblingDll, result, scanManifest: false);
        }

        return result;
    }

    private static void ScanBinaryMarkers(string filePath, BinaryInspectionResult result, bool scanManifest)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (len <= 64)
                return;

            const int headLimit = 2 * 1024 * 1024; // 2 MB
            int headLen = (int)Math.Min(len, headLimit);
            byte[] head = new byte[headLen];
            int readHead = fs.Read(head, 0, headLen);
            AnalyzeByteChunk(head.AsSpan(0, readHead), result, scanManifest);

            if (len > headLimit)
            {
                const int tailLimit = 1024 * 1024; // Last 1 MB (where Win32 .rsrc manifests usually live!)
                int tailLen = (int)Math.Min(len - headLimit, tailLimit);
                fs.Seek(len - tailLen, SeekOrigin.Begin);
                byte[] tail = new byte[tailLen];
                int readTail = fs.Read(tail, 0, tailLen);
                AnalyzeByteChunk(tail.AsSpan(0, readTail), result, scanManifest && result.ManifestAwareness == DpiAwarenessKind.Unknown);
            }
        }
        catch
        {
            // Ignore locked or unreadable protected files
        }
    }

    private static void AnalyzeByteChunk(ReadOnlySpan<byte> data, BinaryInspectionResult result, bool scanManifest)
    {
        string latin1 = Encoding.Latin1.GetString(data);

        if (!result.IsWpf && (latin1.Contains("PresentationFramework", StringComparison.Ordinal) ||
                              latin1.Contains("PresentationCore", StringComparison.Ordinal) ||
                              latin1.Contains("System.Windows.Window", StringComparison.Ordinal)))
        {
            result.IsWpf = true;
        }

        if (!result.IsWinForms && latin1.Contains("System.Windows.Forms", StringComparison.Ordinal))
        {
            result.IsWinForms = true;
        }

        if (!result.IsWinUi && latin1.Contains("Microsoft.UI.Xaml", StringComparison.Ordinal))
        {
            result.IsWinUi = true;
        }

        if (!result.HasQt6Marker && latin1.Contains("Qt6Core", StringComparison.Ordinal))
        {
            result.HasQt6Marker = true;
        }

        if (!result.HasQtScaleMarker && (latin1.Contains("QT_SCALE_FACTOR_ROUNDING_POLICY", StringComparison.Ordinal) ||
                                         latin1.Contains("QT_SCALE_FACTOR", StringComparison.Ordinal)))
        {
            result.HasQtScaleMarker = true;
        }

        if (!result.HasGtkMarker && latin1.Contains("GDK_DPI_SCALE", StringComparison.Ordinal))
        {
            result.HasGtkMarker = true;
        }

        if (!result.HasWebView2Marker && (latin1.Contains("WebView2Loader", StringComparison.OrdinalIgnoreCase) ||
                                          latin1.Contains("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", StringComparison.Ordinal)))
        {
            result.HasWebView2Marker = true;
        }

        if (scanManifest && result.ManifestAwareness == DpiAwarenessKind.Unknown)
        {
            int asmStart = latin1.LastIndexOf("<assembly", StringComparison.OrdinalIgnoreCase);
            if (asmStart >= 0)
            {
                int asmEnd = latin1.IndexOf("</assembly>", asmStart, StringComparison.OrdinalIgnoreCase);
                if (asmEnd > asmStart && (asmEnd - asmStart) < 32768)
                {
                    string manifestXml = latin1.Substring(asmStart, asmEnd - asmStart + 11);
                    result.ManifestAwareness = ParseManifestDpiAwareness(manifestXml);
                }
            }
        }
    }

    private static DpiAwarenessKind ParseManifestDpiAwareness(string xml)
    {
        if (xml.Contains("PerMonitorV2", StringComparison.OrdinalIgnoreCase))
            return DpiAwarenessKind.PerMonitorV2;

        if (xml.Contains("PerMonitor", StringComparison.OrdinalIgnoreCase) ||
            xml.Contains("true/PM", StringComparison.OrdinalIgnoreCase) ||
            xml.Contains("true/pm", StringComparison.OrdinalIgnoreCase))
            return DpiAwarenessKind.PerMonitor;

        if (xml.Contains("<dpiAware", StringComparison.OrdinalIgnoreCase) &&
            xml.Contains(">true<", StringComparison.OrdinalIgnoreCase))
            return DpiAwarenessKind.System;

        if (xml.Contains(">system<", StringComparison.OrdinalIgnoreCase))
            return DpiAwarenessKind.System;

        if (xml.Contains(">unaware<", StringComparison.OrdinalIgnoreCase) ||
            xml.Contains(">false<", StringComparison.OrdinalIgnoreCase))
            return DpiAwarenessKind.Unaware;

        return DpiAwarenessKind.Unknown;
    }
}
