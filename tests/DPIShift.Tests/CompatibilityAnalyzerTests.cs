using System.Text;
using DPIShift.Core.Detection;
using DPIShift.Core.Models;

namespace DPIShift.Tests;

public sealed class CompatibilityAnalyzerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly CompatibilityAnalyzer _analyzer = new();

    public CompatibilityAnalyzerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DPIShiftTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void Detects_ChromiumBrowser_WithVersionSubfolder()
    {
        string dir = CreateSubDir("ChromeApp");
        string exe = CreateDummyBinary(Path.Combine(dir, "chrome.exe"), " MZ dummy ");
        string verDir = Directory.CreateDirectory(Path.Combine(dir, "131.0.6778.86")).FullName;
        File.WriteAllText(Path.Combine(verDir, "chrome.dll"), "dll");
        File.WriteAllText(Path.Combine(verDir, "chrome_100_percent.pak"), "pak");

        CompatibilityReport report = _analyzer.Analyze(exe);
        Assert.Equal(ScalingTech.Chromium, report.Technology);
        Assert.Equal(SupportLevel.Full, report.Support);
        Assert.Equal(ScalingDeliveryMode.ChromiumFlags, report.DeliveryMode);
        Assert.True(report.CanScale);
    }

    [Fact]
    public void Detects_DiscordSquirrelElectron_FromBothUpdaterAndInnerApp()
    {
        string discordRoot = CreateSubDir("Discord");
        string updateExe = CreateDummyBinary(Path.Combine(discordRoot, "Update.exe"), "Squirrel");
        string appVerDir = Directory.CreateDirectory(Path.Combine(discordRoot, "app-1.0.9175")).FullName;
        string discordExe = CreateDummyBinary(Path.Combine(appVerDir, "Discord.exe"), "Electron");
        Directory.CreateDirectory(Path.Combine(appVerDir, "resources"));
        File.WriteAllText(Path.Combine(appVerDir, "resources", "app.asar"), "asar");

        CompatibilityReport fromUpdater = _analyzer.Analyze(updateExe);
        CompatibilityReport fromRunningExe = _analyzer.Analyze(discordExe);

        Assert.Equal(ScalingTech.ElectronSquirrel, fromUpdater.Technology);
        Assert.Equal(ScalingTech.ElectronSquirrel, fromRunningExe.Technology);
        Assert.Equal(SupportLevel.Full, fromRunningExe.Support);
        Assert.Equal("Discord.exe", fromUpdater.SquirrelProcessName);
        Assert.Equal("Discord.exe", fromRunningExe.SquirrelProcessName);

        // Both must produce the exact same canonical ID across version updates!
        string id1 = CompatibilityAnalyzer.NormalizeCanonicalId(updateExe, fromUpdater);
        string id2 = CompatibilityAnalyzer.NormalizeCanonicalId(discordExe, fromRunningExe);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Detects_JetBrainsDataGrip_AndResolvesUserVmOptionsPath()
    {
        string dgRoot = CreateSubDir("DataGrip");
        string binDir = Directory.CreateDirectory(Path.Combine(dgRoot, "bin")).FullName;
        Directory.CreateDirectory(Path.Combine(dgRoot, "jbr", "bin"));
        string exe = CreateDummyBinary(Path.Combine(binDir, "datagrip64.exe"), "JetBrains Launcher");

        File.WriteAllText(Path.Combine(dgRoot, "product-info.json"), """
        {
          "name": "DataGrip",
          "productVendor": "JetBrains",
          "dataDirectoryName": "DataGrip2026.1"
        }
        """);

        CompatibilityReport report = _analyzer.Analyze(exe);
        Assert.Equal(ScalingTech.JetBrainsIde, report.Technology);
        Assert.Equal(SupportLevel.Full, report.Support);
        Assert.Equal(ScalingDeliveryMode.JetBrainsVmOptions, report.DeliveryMode);
        Assert.NotNull(report.JetBrainsVmOptionsPath);
        Assert.EndsWith(Path.Combine("JetBrains", "DataGrip2026.1", "datagrip64.exe.vmoptions"), report.JetBrainsVmOptionsPath!);
    }

    [Fact]
    public void Detects_Qt6_EclipseSwt_Gtk_Wpf_Game_And_LegacyWin32()
    {
        // Qt 6
        string qtDir = CreateSubDir("ObsApp");
        string qtExe = CreateDummyBinary(Path.Combine(qtDir, "obs64.exe"), "QtApp");
        File.WriteAllText(Path.Combine(qtDir, "Qt6Core.dll"), "qt6");
        CompatibilityReport qtReport = _analyzer.Analyze(qtExe);
        Assert.Equal(ScalingTech.Qt6, qtReport.Technology);
        Assert.Equal(SupportLevel.Full, qtReport.Support);

        // Eclipse / DBeaver SWT
        string dbDir = CreateSubDir("DBeaver");
        string dbExe = CreateDummyBinary(Path.Combine(dbDir, "dbeaver.exe"), "equinox");
        File.WriteAllText(Path.Combine(dbDir, "dbeaver.ini"), "-startup\nplugins\n-vmargs\n-Xmx1024m");
        CompatibilityReport dbReport = _analyzer.Analyze(dbExe);
        Assert.Equal(ScalingTech.EclipseSwt, dbReport.Technology);
        Assert.Equal(SupportLevel.Full, dbReport.Support);

        // GTK
        string gtkDir = CreateSubDir("GimpApp");
        string gtkExe = CreateDummyBinary(Path.Combine(gtkDir, "gimp.exe"), "gtk");
        File.WriteAllText(Path.Combine(gtkDir, "libgtk-3-0.dll"), "gtk3");
        CompatibilityReport gtkReport = _analyzer.Analyze(gtkExe);
        Assert.Equal(ScalingTech.Gtk, gtkReport.Technology);
        Assert.Equal(SupportLevel.Partial, gtkReport.Support);

        // .NET WPF (apphost .exe + managed .dll sibling)
        string wpfDir = CreateSubDir("ModernWpfApp");
        string wpfExe = CreateDummyBinary(Path.Combine(wpfDir, "SampleWpf.exe"), "apphost");
        CreateDummyBinary(Path.Combine(wpfDir, "SampleWpf.dll"), "Metadata PresentationFramework System.Windows.Window");
        CompatibilityReport wpfReport = _analyzer.Analyze(wpfExe);
        Assert.Equal(ScalingTech.Wpf, wpfReport.Technology);
        Assert.Equal(SupportLevel.Full, wpfReport.Support);
        Assert.False(wpfReport.RequiresRestart);

        // Game / Unity (Protected - Unsupported)
        string gameDir = CreateSubDir("UnityGame");
        string gameExe = CreateDummyBinary(Path.Combine(gameDir, "CyberGame.exe"), "game");
        File.WriteAllText(Path.Combine(gameDir, "UnityPlayer.dll"), "unity");
        CompatibilityReport gameReport = _analyzer.Analyze(gameExe);
        Assert.Equal(ScalingTech.GameOrAntiCheat, gameReport.Technology);
        Assert.Equal(SupportLevel.Unsupported, gameReport.Support);
        Assert.False(gameReport.CanScale);

        // Legacy Win32 (Unaware)
        string legDir = CreateSubDir("OldWin32");
        string legExe = CreateDummyBinary(Path.Combine(legDir, "OldTool98.exe"), "Win32 GDI App");
        CompatibilityReport legReport = _analyzer.Analyze(legExe);
        Assert.Equal(ScalingTech.LegacySystemOrUnaware, legReport.Technology);
        Assert.Equal(SupportLevel.Unsupported, legReport.Support);
        Assert.False(legReport.CanScale);
    }

    private string CreateSubDir(string name) =>
        Directory.CreateDirectory(Path.Combine(_tempRoot, name)).FullName;

    private static string CreateDummyBinary(string path, string markerContent)
    {
        byte[] payload = Encoding.Latin1.GetBytes(markerContent.PadRight(256, '\0'));
        File.WriteAllBytes(path, payload);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { }
    }
}
