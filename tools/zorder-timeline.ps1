Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WB {
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static string At(int x, int y) { var h = WindowFromPoint(new POINT{X=x,Y=y}); var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString().Substring(0, Math.Min(20, sb.Length)); }
  public static string Cls(IntPtr h) { var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
"@
function S($l) { "{0,-14} at-strip={1,-22} fg={2}" -f $l, [WB]::At(2100,1416), [WB]::Cls([WB]::GetForegroundWindow()) }
function Click($x,$y) { [void][WB]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 80; [WB]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [WB]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
function Esc { [WB]::keybd_event(0x1B,0,0,[UIntPtr]::Zero); [WB]::keybd_event(0x1B,0,2,[UIntPtr]::Zero) }
S "fresh"
Click 830 1416; Start-Sleep -Milliseconds 100; S "start+0.1s"; Start-Sleep -Milliseconds 600; S "start+0.7s"
Esc; foreach ($t in 0.2,0.5,1,2,4) { Start-Sleep -Milliseconds ([int]($t*1000) - [int]($t*500)); S "esc+${t}s" }
Click 1280 700; Start-Sleep -Milliseconds 500; S "click app win"
Click 2500 1416; Start-Sleep -Milliseconds 700; S "clock open"
Esc; Start-Sleep -Milliseconds 1000; S "esc+1s"
Click 1280 700; Start-Sleep -Milliseconds 500; S "click app win"
