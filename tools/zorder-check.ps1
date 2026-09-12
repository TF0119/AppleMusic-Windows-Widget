
# Checks whether the strip is visible/above Shell_TrayWnd, clicks an empty taskbar spot, checks again.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$widgetPid = (Get-Process AppleMusicWidget -ErrorAction Stop).Id
function TopmostOrder {
  # walk from topmost (GW_HWNDFIRST=0) downward via GW_HWNDNEXT=2
  $h = [W]::GetWindow([W]::FindWindow("Shell_TrayWnd", $null), 0)
  $i = 0; $out = @()
  while ($h -ne [IntPtr]::Zero -and $i -lt 400) {
    if ([W]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256; [void][W]::GetClassName($h, $sb, 256)
      $p = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$p)
      $cls = $sb.ToString()
      if ($cls -eq "Shell_TrayWnd" -or $p -eq $widgetPid) {
        $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r)
        $out += "  #$i $cls pid=$p rect=$($r.Left),$($r.Top)-$($r.Right),$($r.Bottom)"
      }
    }
    $h = [W]::GetWindow($h, 2); $i++
  }
  $out
}
"before:"; TopmostOrder
"fg=" + [W]::GetForegroundWindow()
$tray = [W]::FindWindow("Shell_TrayWnd", $null); $r = New-Object W+RECT; [void][W]::GetWindowRect($tray, [ref]$r)
$x = $r.Left + 200; $y = ($r.Top + $r.Bottom) / 2   # far-left empty area (right of widgets? left of Start)
[void][W]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 100
[W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800
"after click at ${x},${y}:"; TopmostOrder
"fg=" + [W]::GetForegroundWindow()
