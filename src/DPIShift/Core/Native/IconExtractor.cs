using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DPIShift.Core.Native;

internal static class IconExtractor
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIconForExecutable(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        return Cache.GetOrAdd(exePath, ExtractFrozenIcon);
    }

    public static void ClearCache() => Cache.Clear();

    private static ImageSource? ExtractFrozenIcon(string path)
    {
        if (!File.Exists(path))
            return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var large = new IntPtr[1];
            uint count = Win32Interop.ExtractIconExW(path, 0, large, null, 1);
            if (count > 0 && large[0] != IntPtr.Zero)
            {
                hIcon = large[0];
            }
            else
            {
                var shfi = new Win32Interop.SHFILEINFOW();
                IntPtr res = Win32Interop.SHGetFileInfoW(
                    path,
                    0,
                    ref shfi,
                    (uint)Marshal.SizeOf<Win32Interop.SHFILEINFOW>(),
                    SHGFI_ICON | SHGFI_LARGEICON);

                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    hIcon = shfi.hIcon;
            }

            if (hIcon == IntPtr.Zero)
                return null;

            BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
                Win32Interop.DestroyIcon(hIcon);
        }
    }
}
