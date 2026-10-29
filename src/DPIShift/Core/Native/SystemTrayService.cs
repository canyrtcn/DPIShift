using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DPIShift.Core.Native;

public sealed class SystemTrayService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    private const uint TrayUid = 1001;
    private const uint NIIF_INFO = 0x00000001;

    private HwndSource? _sinkSource;
    private IntPtr _hIcon;
    private bool _iconAdded;
    private uint _taskbarCreatedMsg;
    private string _tooltip = "DPIShift — Uygulama Bazlı Ekran Ölçeklendirme";

    public event Action? OpenRequested;
    public event Action? ContextMenuRequested;

    public void Initialize()
    {
        if (_sinkSource is not null)
            return;

        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");

        var parameters = new HwndSourceParameters("DPIShift_TrayMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP (hidden, never shown)
            ExtendedWindowStyle = (int)Win32Interop.WS_EX_TOOLWINDOW
        };

        _sinkSource = new HwndSource(parameters);
        _sinkSource.AddHook(WndProc);

        LoadEmbeddedProcessIcon();
        AddOrRefreshTrayIcon();
    }

    public void UpdateTooltip(string tooltip)
    {
        _tooltip = string.IsNullOrWhiteSpace(tooltip) ? "DPIShift" : tooltip;
        if (_tooltip.Length > 120)
            _tooltip = _tooltip[..120];

        if (!_iconAdded || _sinkSource is null)
            return;

        var data = CreateNotifyData();
        data.uFlags = Win32Interop.NIF_TIP;
        Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_MODIFY, ref data);
    }

    public void ShowBalloonNotification(string title, string message)
    {
        if (!_iconAdded || _sinkSource is null)
            return;

        var data = CreateNotifyData();
        data.uFlags = Win32Interop.NIF_INFO;
        data.szInfoTitle = title.Length > 60 ? title[..60] : title;
        data.szInfo = message.Length > 250 ? message[..250] : message;
        data.dwInfoFlags = NIIF_INFO;
        Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_MODIFY, ref data);
    }

    private void LoadEmbeddedProcessIcon()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var small = new IntPtr[1];
        var large = new IntPtr[1];
        uint extracted = Win32Interop.ExtractIconExW(exePath, 0, large, small, 1);
        if (extracted > 0)
        {
            if (small[0] != IntPtr.Zero)
            {
                _hIcon = small[0];
                if (large[0] != IntPtr.Zero)
                    Win32Interop.DestroyIcon(large[0]);
            }
            else if (large[0] != IntPtr.Zero)
            {
                _hIcon = large[0];
            }
        }
    }

    private void AddOrRefreshTrayIcon()
    {
        if (_sinkSource is null)
            return;

        var data = CreateNotifyData();
        data.uFlags = Win32Interop.NIF_MESSAGE | Win32Interop.NIF_TIP | (_hIcon != IntPtr.Zero ? Win32Interop.NIF_ICON : 0);

        if (_iconAdded)
        {
            if (!Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_MODIFY, ref data))
                _iconAdded = Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_ADD, ref data);
        }
        else
        {
            _iconAdded = Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_ADD, ref data);
        }
    }

    private Win32Interop.NOTIFYICONDATAW CreateNotifyData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32Interop.NOTIFYICONDATAW>(),
        hWnd = _sinkSource?.Handle ?? IntPtr.Zero,
        uID = TrayUid,
        uCallbackMessage = Win32Interop.WM_TRAYICON,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        uint uMsg = (uint)msg;
        if (_taskbarCreatedMsg != 0 && uMsg == _taskbarCreatedMsg)
        {
            _iconAdded = false;
            AddOrRefreshTrayIcon();
            return IntPtr.Zero;
        }

        if (uMsg == Win32Interop.WM_TRAYICON)
        {
            uint mouseEvent = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseEvent is Win32Interop.WM_LBUTTONUP or Win32Interop.WM_LBUTTONDBLCLK)
            {
                handled = true;
                OpenRequested?.Invoke();
            }
            else if (mouseEvent is Win32Interop.WM_RBUTTONUP or Win32Interop.WM_CONTEXTMENU)
            {
                handled = true;
                ContextMenuRequested?.Invoke();
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_iconAdded && _sinkSource is not null)
        {
            var data = CreateNotifyData();
            Win32Interop.Shell_NotifyIconW(Win32Interop.NIM_DELETE, ref data);
            _iconAdded = false;
        }

        if (_sinkSource is not null)
        {
            _sinkSource.RemoveHook(WndProc);
            _sinkSource.Dispose();
            _sinkSource = null;
        }

        if (_hIcon != IntPtr.Zero)
        {
            Win32Interop.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
