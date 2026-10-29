using System.ComponentModel;

namespace DPIShift.Core.Localization;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _currentLanguage = "en";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetLanguage(value);
    }

    public static bool IsTurkish => string.Equals(Instance._currentLanguage, "tr", StringComparison.OrdinalIgnoreCase);
    public bool IsEnglish => !IsTurkish;
    public bool IsTrSelected => IsTurkish;
    public string LanguageBadge => IsTurkish ? "TR" : "EN";

    public void SetLanguage(string? lang)
    {
        string normalized = string.Equals(lang?.Trim(), "tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
        if (string.Equals(_currentLanguage, normalized, StringComparison.Ordinal))
            return;

        _currentLanguage = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke();
    }

    public void ToggleLanguage() => SetLanguage(IsTurkish ? "en" : "tr");

    public static string Pick(string en, string tr) => IsTurkish ? tr : en;

    // Window & Header
    public string WindowTitle => Pick("DPIShift — Per-Application Display Scaling", "DPIShift — Uygulama Bazlı Ekran Ölçeklendirme");
    public string HeaderSubtitle => Pick("Per-Application DPI Scaling", "Uygulama Bazlı DPI Ölçeklendirme");
    public string SettingsButton => Pick("Settings", "Ayarlar");
    public string LanguageTooltip => Pick("Switch interface language (EN / TR)", "Arayüz dilini değiştir (EN / TR)");
    public string MinimizeTooltip => Pick("Minimize", "Küçült");
    public string MaximizeRestoreTooltip => Pick("Maximize / Restore", "Ekranı Kapla / Geri Yükle");
    public string CloseTooltip => Pick("Close", "Kapat");

    // Left Sidebar
    public string SearchPlaceholder => Pick("Search applications...", "Uygulama ara...");
    public string BrowseExeButton => Pick("Select .exe", ".exe Seç");
    public string BrowseExeTooltip => Pick("Select an executable (.exe) from disk", "Diskten çalıştırılabilir (.exe) uygulama seçin");
    public string RefreshTooltip => Pick("Refresh installed and running applications", "Uygulama ve çalışan pencere listesini yenile");
    public string TabAllPrefix => Pick("All (", "Tümü (");
    public string TabRunningPrefix => Pick("Running (", "Çalışan (");
    public string TabConfiguredPrefix => Pick("Custom (", "Özel (");
    public string ShowSupportedOnlyLabel => Pick("Show scalable applications only", "Yalnızca ölçeklenebilir uygulamaları göster");
    public string RunningDotTooltip => Pick("Currently running", "Şu anda çalışıyor");
    public string LoadingAppsText => Pick("Analyzing applications...", "Uygulamalar analiz ediliyor...");

    // Right Workspace & Cards
    public string EmptySelectionText => Pick("Select an application from the list or add one via 'Select .exe'", "Sol listeden bir uygulama seçin veya '.exe Seç' ile ekleyin");
    public string FrameworkPrefix => Pick("Framework: ", "Altyapı: ");
    public string DpiModePrefix => Pick("DPI Mode: ", "DPI Modu: ");
    public string OpenLocationButton => Pick("Open Folder", "Konumu Aç");
    public string ScaleSectionTitle => Pick("Interface / DPI Scale Factor", "Arayüz / DPI Ölçek Oranı");
    public string QuickPresetsHeader => Pick("QUICK SCALE PRESETS", "HIZLI ÖLÇEK SEÇİMİ");
    public string ScaleComparisonLabel => Pick("Scale Comparison", "Ölçek Karşılaştırması");
    public string TrueVectorRenderingLabel => Pick("True Vector Rendering", "Gerçek Vektörel Çizim");
    public string BaseReferenceTooltip => Pick("Base 100% Reference Size", "Baz %100 Referans Boyutu");
    public string TargetScaleTooltip => Pick("Target Application Scale", "Hedef Uygulama Ölçeği");
    public string UpdateShortcutsToggle => Pick("Automatically update Desktop, Start Menu, and Taskbar shortcuts (Backed up)", "Masaüstü, Başlat Menüsü ve Görev Çubuğu kısayollarını otomatik güncelle (Yedeklenir)");
    public string AutoApplyRuntimeToggle => Pick("Automatically apply this DPI scale in the background when the window opens", "Uygulama penceresi açıldığında arka planda otomatik olarak bu DPI ölçeğini uygula");

    // Action Bar Buttons
    public string ApplyAndRestartButton => Pick("Apply & Restart Now", "Uygula ve Şimdi Yeniden Başlat");
    public string LaunchScaledButton => Pick("Launch Scaled", "Ölçekli Başlat");
    public string DesktopShortcutButton => Pick("Desktop Shortcut", "Masaüstü Kısayolu");
    public string ResetToDefaultButton => Pick("Reset to Default (100%)", "Varsayılana Döndür (%100)");

    // Settings Drawer
    public string SettingsDrawerTitle => Pick("Application & System Tray", "Uygulama ve Sistem Tepsisi");
    public string CloseDrawerButton => Pick("Close", "Kapat");
    public string LanguageSectionHeader => Pick("INTERFACE LANGUAGE", "ARAYÜZ DİLİ");
    public string TraySectionHeader => Pick("SYSTEM TRAY", "SİSTEM TEPSİSİ (SYSTEM TRAY)");
    public string CloseToTrayToggle => Pick("Minimize to system tray when closing window (X)", "Pencere kapatıldığında (X) sistem tepsisine küçült");
    public string MinimizeToTrayToggle => Pick("Hide to system tray when minimized (—)", "Simge durumuna küçültüldüğünde (—) tepsiye gizle");
    public string StartWithWindowsToggle => Pick("Launch automatically in system tray on Windows startup", "Windows açılışında sistem tepsisinde otomatik başlat");
    public string PerformanceSectionHeader => Pick("PERFORMANCE & BACKGROUND WATCHER", "PERFORMANS VE ARKA PLAN İZLEYİCİ");
    public string EnableWatcherToggle => Pick("Keep live window DPI watcher active (0% idle CPU)", "Canlı pencere DPI izleyicisini etkin tut (%0 boşta CPU)");
    public string WatcherExplanationText => Pick(
        "Uses event hooks with zero background polling or DLL injection. Does not affect gaming performance or anti-cheat systems; unhooks automatically when no live rules are active.",
        "Sürekli döngü (polling) veya DLL enjeksiyonu kullanılmaz. Oyun performansını ve hile koruma sistemlerini etkilemez; etkin canlı kural yoksa kanca devre dışı kalır.");
    public string ResetSectionHeader => Pick("RESET RULES", "KURALLARI SIFIRLAMA");
    public string ResetAllRulesButton => Pick("Reset All Application Scales & Restore Defaults", "Tüm Uygulama Ölçeklerini Sıfırla ve Varsayılana Döndür");
    public string CompleteExitButton => Pick("Quit DPIShift Completely (Exit Tray)", "DPIShift'i Tamamen Kapat (Tepsiden Çık)");

    // Tray Popup
    public string TrayQuickLaunchHeader => Pick("QUICK LAUNCH SCALED", "ÖLÇEKLİ HIZLI BAŞLAT");
    public string TrayOpenUiButton => Pick("Open DPIShift", "DPIShift Arayüzünü Aç");
    public string TrayExitButton => Pick("Quit Application", "Uygulamadan Tamamen Çık");
}
