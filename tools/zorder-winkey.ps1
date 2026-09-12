Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WK {
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static string At(int x, int y) { var h = WindowFromPoint(new POINT{X=x,Y=y}); var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString().Substring(0, Math.Min(20, sb.Length)); }
  public static string Fg { get { var sb = new System.Text.StringBuilder(256); GetClassName(GetForegroundWindow(), sb, 256); return sb.ToString(); } }
}
"@
function S($l) { "{0,-12} at-strip={1,-22} fg={2}" -f $l, [WK]::At(2100,1416), [WK]::Fg }
S "fresh"
[WK]::keybd_event(0x5B,0,0,[UIntPtr]::Zero); [WK]::keybd_event(0x5B,0,2,[UIntPtr]::Zero)  # LWIN opens Start
foreach ($t in 0.3, 1) { Start-Sleep -Milliseconds 400; S "winkey+$t" }
[WK]::keybd_event(0x1B,0,0,[UIntPtr]::Zero); [WK]::keybd_event(0x1B,0,2,[UIntPtr]::Zero)  # Esc closes
foreach ($t in 0.5, 1, 2, 3, 5, 8, 12) { Start-Sleep -Milliseconds 800; S "esc+${t}" }
