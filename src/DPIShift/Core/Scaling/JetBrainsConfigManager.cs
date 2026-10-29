using System.Text;
using DPIShift.Core.Models;

namespace DPIShift.Core.Scaling;

public static class JetBrainsConfigManager
{
    private const string ManagedComment = "# [DPIShift] Managed Per-App UI Scale";

    public static bool ApplyVmOptionsScale(AppScaleProfile profile, int scalePercent, string backupsDirectory)
    {
        string? vmPath = profile.JetBrainsVmOptionsPath;
        if (string.IsNullOrWhiteSpace(vmPath))
            return false;

        try
        {
            string? dir = Path.GetDirectoryName(vmPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bool existedBefore = File.Exists(vmPath);
            if (string.IsNullOrEmpty(profile.ModifiedConfigFilePath))
            {
                profile.ModifiedConfigFilePath = vmPath;
                profile.CreatedConfigFileFromScratch = !existedBefore;

                if (existedBefore)
                {
                    Directory.CreateDirectory(backupsDirectory);
                    string backupFile = Path.Combine(backupsDirectory, GetSafeBackupFileName(profile.Id) + ".vmoptions.bak");
                    if (!File.Exists(backupFile))
                        File.Copy(vmPath, backupFile, overwrite: false);
                }
            }

            List<string> lines = existedBefore
                ? File.ReadAllLines(vmPath, Encoding.UTF8).ToList()
                : [];

            RemoveManagedLines(lines);

            string factor = ScaleMath.FormatAbsoluteFactorFromPercent(scalePercent);
            lines.Add(ManagedComment);
            lines.Add("-Dsun.java2d.uiScale.enabled=true");
            lines.Add($"-Dsun.java2d.uiScale={factor}");

            File.WriteAllLines(vmPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RevertVmOptionsScale(AppScaleProfile profile, string backupsDirectory)
    {
        string? vmPath = profile.ModifiedConfigFilePath ?? profile.JetBrainsVmOptionsPath;
        if (string.IsNullOrWhiteSpace(vmPath) || !File.Exists(vmPath))
        {
            ClearProfileConfigTracking(profile);
            return true;
        }

        try
        {
            List<string> lines = File.ReadAllLines(vmPath, Encoding.UTF8).ToList();
            RemoveManagedLines(lines);

            bool hasRemainingContent = lines.Any(l => !string.IsNullOrWhiteSpace(l));
            if (!hasRemainingContent && profile.CreatedConfigFileFromScratch)
            {
                File.Delete(vmPath);
            }
            else
            {
                File.WriteAllLines(vmPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            string backupFile = Path.Combine(backupsDirectory, GetSafeBackupFileName(profile.Id) + ".vmoptions.bak");
            if (File.Exists(backupFile))
            {
                try { File.Delete(backupFile); } catch { }
            }

            ClearProfileConfigTracking(profile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearProfileConfigTracking(AppScaleProfile profile)
    {
        profile.ModifiedConfigFilePath = null;
        profile.CreatedConfigFileFromScratch = false;
    }

    private static void RemoveManagedLines(List<string> lines)
    {
        lines.RemoveAll(line =>
        {
            string trimmed = line.Trim();
            return trimmed.Equals(ManagedComment, StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("-Dsun.java2d.uiScale=", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("-Dsun.java2d.uiScale.enabled=", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("-Dide.ui.scale=", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string GetSafeBackupFileName(string id)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(id);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }
}
