using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DPIShift.Core.Native;

public sealed class ShellLinkInfo
{
    public string ShortcutPath { get; init; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconLocation { get; set; } = string.Empty;
    public int IconIndex { get; set; }
}

internal static class ShellLinkInterop
{
    private const uint STGM_READ = 0x00000000;
    private const int SLGP_RAWPATH = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public static ShellLinkInfo? TryReadShortcut(string lnkPath)
    {
        if (string.IsNullOrWhiteSpace(lnkPath) || !File.Exists(lnkPath))
            return null;

        object? comObj = null;
        try
        {
            comObj = new ShellLinkCoClass();
            var link = (IShellLinkW)comObj;
            var persist = (IPersistFile)comObj;
            persist.Load(lnkPath, (int)STGM_READ);

            var targetSb = new StringBuilder(1024);
            link.GetPath(targetSb, targetSb.Capacity, IntPtr.Zero, 0);
            string target = targetSb.ToString();

            if (string.IsNullOrWhiteSpace(target))
            {
                targetSb.Clear();
                link.GetPath(targetSb, targetSb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
                target = Environment.ExpandEnvironmentVariables(targetSb.ToString());
            }

            var argsSb = new StringBuilder(2048);
            link.GetArguments(argsSb, argsSb.Capacity);

            var workSb = new StringBuilder(1024);
            link.GetWorkingDirectory(workSb, workSb.Capacity);

            var descSb = new StringBuilder(512);
            link.GetDescription(descSb, descSb.Capacity);

            var iconSb = new StringBuilder(1024);
            link.GetIconLocation(iconSb, iconSb.Capacity, out int iconIndex);

            return new ShellLinkInfo
            {
                ShortcutPath = lnkPath,
                TargetPath = target,
                Arguments = argsSb.ToString(),
                WorkingDirectory = workSb.ToString(),
                Description = descSb.ToString(),
                IconLocation = iconSb.ToString(),
                IconIndex = iconIndex
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (comObj is not null && Marshal.IsComObject(comObj))
                Marshal.FinalReleaseComObject(comObj);
        }
    }

    public static bool TryWriteShortcut(ShellLinkInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.ShortcutPath) || string.IsNullOrWhiteSpace(info.TargetPath))
            return false;

        object? comObj = null;
        try
        {
            string? dir = Path.GetDirectoryName(info.ShortcutPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            comObj = new ShellLinkCoClass();
            var link = (IShellLinkW)comObj;
            var persist = (IPersistFile)comObj;

            if (File.Exists(info.ShortcutPath))
            {
                // Load existing shortcut first so hotkeys, AppUserModelID properties, and showCmd are preserved!
                persist.Load(info.ShortcutPath, 2 /* STGM_READWRITE */);
            }

            link.SetPath(info.TargetPath);
            link.SetArguments(info.Arguments ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(info.WorkingDirectory))
                link.SetWorkingDirectory(info.WorkingDirectory);

            if (!string.IsNullOrWhiteSpace(info.Description))
                link.SetDescription(info.Description);

            if (!string.IsNullOrWhiteSpace(info.IconLocation))
                link.SetIconLocation(info.IconLocation, info.IconIndex);

            persist.Save(info.ShortcutPath, true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (comObj is not null && Marshal.IsComObject(comObj))
                Marshal.FinalReleaseComObject(comObj);
        }
    }
}
