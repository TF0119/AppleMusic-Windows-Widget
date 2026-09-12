using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

// Spike: host a WPF HwndSource as a WS_CHILD of Shell_TrayWnd and see whether it
// stays visible (and on top of the taskbar) while Start/Search promote the
// taskbar's z-band.
var t = new Thread(Run);
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();

static void Run()
{
    var tray = N.FindWindow("Shell_TrayWnd", null);
    if (tray == IntPtr.Zero) { Console.Error.WriteLine("no tray"); return; }
    N.GetWindowRect(tray, out var tr);
    var scale = N.GetDpiForWindow(tray) / 96.0;

    // Strip slot: left of the notification area (screen x 1959..2299 -> tray
    // client coords, y 0..48).
    int xPx = 1959 - tr.Left, yPx = 0, wPx = 340, hPx = tr.Bottom - tr.Top;

    var p = new HwndSourceParameters("ChildSpike")
    {
        ParentWindow = tray,
        WindowStyle = N.WS_CHILD | N.WS_VISIBLE | N.WS_CLIPSIBLINGS,
        PositionX = (int)(xPx / scale),
        PositionY = (int)(yPx / scale),
        Width = (int)(wPx / scale),
        Height = (int)(hPx / scale),
        UsesPerPixelOpacity = true,
    };
    // Raw GDI child window first: isolates WPF composition vs plain painting.
    var raw = N.CreateRawChild(tray, 1400, 0, 300, hPx);
    Console.WriteLine($"rawchild={raw} err={Marshal.GetLastWin32Error()}");

    var src = new HwndSource(p);
    var border = new System.Windows.Controls.Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0xEE, 0xC0, 0x39, 0x39)),
        Child = new System.Windows.Controls.TextBlock
        {
            Text = "CHILD SPIKE",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };
    Console.WriteLine($"child={src.Handle} parent={tray} rect=({xPx},{yPx},{wPx}x{hPx}) scale={scale}");
    N.GetWindowRect(src.Handle, out var cr);
    Console.WriteLine($"child screen rect={cr.Left},{cr.Top}-{cr.Right},{cr.Bottom} visible={N.IsWindowVisible(src.Handle)}");

    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
    timer.Tick += (_, _) =>
    {
        border.InvalidateVisual();
        N.RedrawWindow(src.Handle, IntPtr.Zero, IntPtr.Zero,
            N.RDW_INVALIDATE | N.RDW_UPDATENOW | N.RDW_ALLCHILDREN);
    };
    timer.Start();
    new Application().Run();
}

static class N
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rgn, uint f);
    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public delegate IntPtr WndProcT(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASS { public uint style; public WndProcT lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClass(ref WNDCLASS c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint ex, string cls, string? t, uint st, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr i, IntPtr lp);
    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? n);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint rgb);
    private static WndProcT? _keepAlive;
    public static IntPtr CreateRawChild(IntPtr parent, int x, int y, int w, int h)
    {
        _keepAlive = DefWindowProc;
        var wc = new WNDCLASS { lpfnWndProc = _keepAlive, hInstance = GetModuleHandle(null), hbrBackground = CreateSolidBrush(0x0022CC22), lpszClassName = "RawChildGreen" };
        RegisterClass(ref wc);
        return CreateWindowEx(0, "RawChildGreen", null, 0x50000000, x, y, w, h, parent, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
}
