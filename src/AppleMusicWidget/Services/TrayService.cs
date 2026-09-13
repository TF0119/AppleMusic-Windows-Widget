using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using AppleMusicWidget.Models;

namespace AppleMusicWidget.Services;

/// <summary>
/// Taskbar tray icon via Shell_NotifyIcon + a hidden top-level HwndSource.
/// Minimal menu per PLAN §9.6; no WinForms dependency.
/// </summary>
public sealed class TrayService : IDisposable
{
    public event Action? ToggleVisibilityRequested;
    public event Action<bool>? LaunchAtStartupChanged;
    public event Action? ExitRequested;

    private const uint NimAdd = 0x0000;
    private const uint NimModify = 0x0001;
    private const uint NimDelete = 0x0002;
    private const uint NimSetVersion = 0x0004;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;
    private const int NotifyIconVersion4 = 4;
    private const int WmApp = 0x8000;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int WsExToolWindow = 0x00000080;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;

    private readonly WidgetSettings _settings;
    private readonly HwndSource _source;
    private readonly IntPtr _hwnd;
    private readonly int _callbackMessage = WmApp + 1;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;

    public TrayService(WidgetSettings settings)
    {
        _settings = settings;
        var p = new HwndSourceParameters("AppleMusicWidgetTray")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = WsExToolWindow,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _hwnd = _source.Handle;
        _taskbarCreatedMessage = Native.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadIcon();
        AddIcon();
    }

    private IntPtr LoadIcon()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
        var h = Native.LoadImage(IntPtr.Zero, path, ImageIcon, 16, 16, LrLoadFromFile);
        if (h == IntPtr.Zero)
            Debug.WriteLine($"[TrayService] LoadImage failed for {path} (err {Marshal.GetLastWin32Error()})");
        return h;
    }

    private void AddIcon()
    {
        var d = new Native.NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = _callbackMessage,
            hIcon = _icon,
            szTip = "Apple Music Widget",
        };
        if (!Native.Shell_NotifyIcon(NimAdd, ref d))
            Debug.WriteLine($"[TrayService] NIM_ADD failed (err {Marshal.GetLastWin32Error()})");
        d.uVersion = NotifyIconVersion4;
        Native.Shell_NotifyIcon(NimSetVersion, ref d);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage)
        {
            var evt = lParam.ToInt32() & 0xFFFF;
            // NOTIFYICON_VERSION_4: single left click arrives as NIN_SELECT; a double
            // click would add WM_LBUTTONDBLCLK on top, so only NIN_SELECT toggles.
            if (evt == NinSelect)
            {
                ToggleVisibilityRequested?.Invoke();
                handled = true;
            }
            else if (evt is WmRButtonUp or WmContextMenu)
            {
                ShowMenu();
                handled = true;
            }
        }
        else if (msg == unchecked((int)_taskbarCreatedMessage))
        {
            // Explorer restarted: re-register the icon.
            AddIcon();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        var toggle = new MenuItem { Header = "表示 / 非表示" };
        toggle.Click += (_, _) => ToggleVisibilityRequested?.Invoke();
        var startup = new MenuItem
        {
            Header = "Windows 起動時に自動起動",
            IsCheckable = true,
            IsChecked = _settings.LaunchAtStartup,
        };
        startup.Click += (_, _) => LaunchAtStartupChanged?.Invoke(startup.IsChecked);
        var exit = new MenuItem { Header = "終了" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(toggle);
        menu.Items.Add(startup);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        // Tray-menu convention: foreground our window so the menu dismisses on outside click.
        Native.SetForegroundWindow(_hwnd);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        var d = new Native.NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
        };
        Native.Shell_NotifyIcon(NimDelete, ref d);
        if (_icon != IntPtr.Zero) { Native.DestroyIcon(_icon); _icon = IntPtr.Zero; }
        _source.Dispose();
    }

    private static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cx, int cy, uint fuLoad);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion; // union: uTimeout / uBalloonTimeout
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
    }
}
