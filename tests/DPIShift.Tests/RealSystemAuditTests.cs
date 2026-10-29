using DPIShift.Core.Detection;
using DPIShift.Core.Models;

namespace DPIShift.Tests;

public sealed class RealSystemAuditTests
{
    [Fact]
    public void AppDiscovery_RunsCleanlyOnLiveWindowsSystem_WithoutLeakingSystemDaemons()
    {
        var analyzer = new CompatibilityAnalyzer();
        var discovery = new AppDiscoveryService(analyzer);

        List<DiscoveredApp> apps = discovery.DiscoverAll();

        Assert.NotNull(apps);
        Assert.NotEmpty(apps);

        foreach (DiscoveredApp app in apps)
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id));
            Assert.False(string.IsNullOrWhiteSpace(app.DisplayName));
            Assert.True(File.Exists(app.ExecutablePath), $"Discovered path should exist: {app.ExecutablePath}");

            string exeName = Path.GetFileName(app.ExecutablePath);
            Assert.NotEqual("svchost.exe", exeName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("dwm.exe", exeName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("csrss.exe", exeName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("explorer.exe", exeName, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RepositorySourceFiles_ContainZeroHardcodedPersonalUserPaths()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DPIShift.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        string repoRoot = dir!;
        string currentUserName = Environment.UserName;

        string[] checkedExtensions = [".cs", ".xaml", ".csproj", ".manifest", ".ps1", ".yml", ".md"];
        string[] ignoredDirs = ["bin", "obj", "dist", "artifacts", ".git", ".vs"];

        foreach (string file in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoRoot, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(seg => ignoredDirs.Contains(seg, StringComparer.OrdinalIgnoreCase)))
                continue;

            string ext = Path.GetExtension(file);
            if (!checkedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            string forbiddenPrefix = "C:" + @"\Users\";
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(forbiddenPrefix, text, StringComparison.OrdinalIgnoreCase);
            if (currentUserName.Length >= 4)
            {
                Assert.DoesNotContain($@"\{currentUserName}\", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
