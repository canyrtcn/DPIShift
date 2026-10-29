using System.Globalization;
using System.Text.RegularExpressions;

namespace DPIShift.Core.Scaling;

public static partial class ScaleMath
{
    public const int MinScalePercent = 50;
    public const int MaxScalePercent = 300;

    public static int ClampPercent(int percent) =>
        Math.Clamp(percent, MinScalePercent, MaxScalePercent);

    public static string FormatFactor(double factor) =>
        factor.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatAbsoluteFactorFromPercent(int scalePercent) =>
        FormatFactor(ClampPercent(scalePercent) / 100.0);

    public static int PercentToWindowsDpi(int scalePercent) =>
        (int)Math.Round(96.0 * ClampPercent(scalePercent) / 100.0);

    /// <summary>
    /// QT_SCALE_FACTOR is multiplicative relative to the OS monitor scale factor.
    /// Dividing desiredPercent by osScalePercent guarantees the exact target UI scale regardless of OS DPI.
    /// </summary>
    public static string FormatRelativeQtFactor(int desiredPercent, int osScalePercent)
    {
        int safeOs = osScalePercent <= 0 ? 100 : osScalePercent;
        double multiplier = (double)ClampPercent(desiredPercent) / safeOs;
        return FormatFactor(multiplier);
    }

    public static (string GdkScale, string GdkDpiScale) FormatGtkFactors(int desiredPercent)
    {
        int clamped = ClampPercent(desiredPercent);
        if (clamped >= 200 && clamped % 100 == 0)
        {
            int intScale = clamped / 100;
            return (intScale.ToString(CultureInfo.InvariantCulture), "1");
        }

        return ("1", FormatFactor(clamped / 100.0));
    }

    public static string BuildChromiumFlags(int scalePercent) =>
        $"--high-dpi-support=1 --force-device-scale-factor={FormatAbsoluteFactorFromPercent(scalePercent)}";

    public static string BuildEclipseAppendVmArgs(int scalePercent)
    {
        int clamped = ClampPercent(scalePercent);
        string factor = FormatAbsoluteFactorFromPercent(clamped);
        return $"--launcher.appendVmargs -vmargs -Dswt.autoScale={clamped.ToString(CultureInfo.InvariantCulture)} -Dsun.java2d.uiScale={factor}";
    }

    public static string InjectChromiumFlags(string? existingArgs, int scalePercent)
    {
        string cleaned = StripChromiumFlags(existingArgs);
        string flags = BuildChromiumFlags(scalePercent);
        return string.IsNullOrWhiteSpace(cleaned) ? flags : $"{cleaned} {flags}";
    }

    public static string StripChromiumFlags(string? existingArgs)
    {
        if (string.IsNullOrWhiteSpace(existingArgs))
            return string.Empty;

        string s = ForceDeviceScaleRegex().Replace(existingArgs, string.Empty);
        s = HighDpiSupportRegex().Replace(s, string.Empty);
        return CollapseSpacesRegex().Replace(s, " ").Trim();
    }

    public static string InjectSquirrelProcessStartArgs(string? existingUpdateArgs, string processName, int scalePercent)
    {
        string flags = BuildChromiumFlags(scalePercent);
        if (string.IsNullOrWhiteSpace(existingUpdateArgs))
        {
            return $"--processStart {processName} --process-start-args \"{flags}\"";
        }

        string cleaned = StripSquirrelChromiumFlags(existingUpdateArgs);
        if (!cleaned.Contains("--processStart", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = $"--processStart {processName} {cleaned}".Trim();
        }

        return $"{cleaned} --process-start-args \"{flags}\"";
    }

    public static string StripSquirrelChromiumFlags(string? existingUpdateArgs)
    {
        if (string.IsNullOrWhiteSpace(existingUpdateArgs))
            return string.Empty;

        string s = SquirrelProcessStartArgsRegex().Replace(existingUpdateArgs, match =>
        {
            string inner = match.Groups[1].Value;
            string strippedInner = StripChromiumFlags(inner);
            return string.IsNullOrWhiteSpace(strippedInner)
                ? string.Empty
                : $"--process-start-args \"{strippedInner}\"";
        });

        return CollapseSpacesRegex().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"--force-device-scale-factor=[^\s""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForceDeviceScaleRegex();

    [GeneratedRegex(@"--high-dpi-support=[^\s""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HighDpiSupportRegex();

    [GeneratedRegex(@"--process-start-args\s+""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SquirrelProcessStartArgsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseSpacesRegex();
}
