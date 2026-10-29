using System.Globalization;
using DPIShift.Core.Scaling;

namespace DPIShift.Tests;

public sealed class ScaleMathTests
{
    [Fact]
    public void Formatting_AlwaysUsesInvariantDotSeparator_UnderTurkishLocale()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal("1.25", ScaleMath.FormatAbsoluteFactorFromPercent(125));
            Assert.Equal("1.1", ScaleMath.FormatAbsoluteFactorFromPercent(110));
            Assert.Equal("1.5", ScaleMath.FormatAbsoluteFactorFromPercent(150));
            Assert.Equal("--high-dpi-support=1 --force-device-scale-factor=1.25", ScaleMath.BuildChromiumFlags(125));
            Assert.Equal("1.2", ScaleMath.FormatRelativeQtFactor(150, 125));
            Assert.Equal("0.8", ScaleMath.FormatRelativeQtFactor(100, 125));

            (string gdkScale, string gdkDpi) = ScaleMath.FormatGtkFactors(135);
            Assert.Equal("1", gdkScale);
            Assert.Equal("1.35", gdkDpi);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(100, 96)]
    [InlineData(125, 120)]
    [InlineData(150, 144)]
    [InlineData(175, 168)]
    [InlineData(200, 192)]
    public void PercentToWindowsDpi_ComputesAccurateDpi(int percent, int expectedDpi)
    {
        Assert.Equal(expectedDpi, ScaleMath.PercentToWindowsDpi(percent));
    }

    [Fact]
    public void ChromiumFlagInjection_PreservesUserArguments_AndStripsCleanly()
    {
        const string originalArgs = "--profile-directory=\"Profile 2\" --restore-last-session";

        string injected125 = ScaleMath.InjectChromiumFlags(originalArgs, 125);
        Assert.Contains("--profile-directory=\"Profile 2\"", injected125);
        Assert.Contains("--force-device-scale-factor=1.25", injected125);

        // Re-applying 150% must replace 1.25 rather than duplicating flags
        string injected150 = ScaleMath.InjectChromiumFlags(injected125, 150);
        Assert.DoesNotContain("1.25", injected150);
        Assert.Contains("--force-device-scale-factor=1.5", injected150);

        string stripped = ScaleMath.StripChromiumFlags(injected150);
        Assert.Equal(originalArgs, stripped);
    }

    [Fact]
    public void SquirrelArguments_InjectAndStripCleanly()
    {
        const string original = "--processStart Discord.exe";

        string injected = ScaleMath.InjectSquirrelProcessStartArgs(original, "Discord.exe", 110);
        Assert.Equal("--processStart Discord.exe --process-start-args \"--high-dpi-support=1 --force-device-scale-factor=1.1\"", injected);

        string updated = ScaleMath.InjectSquirrelProcessStartArgs(injected, "Discord.exe", 125);
        Assert.DoesNotContain("1.1", updated);
        Assert.Contains("--force-device-scale-factor=1.25", updated);

        string stripped = ScaleMath.StripSquirrelChromiumFlags(updated);
        Assert.Equal(original, stripped);
    }
}
