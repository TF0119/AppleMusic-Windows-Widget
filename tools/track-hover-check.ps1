Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WI {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
}
"@
function Click($x,$y) { [void][WI]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 80; [WI]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [WI]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
function Shot($n) { $b = New-Object System.Drawing.Bitmap 700,200; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(1850,1190,0,0,$b.Size); $g.Dispose(); $b.Save("$PSScriptRoot\out\$n.png"); $b.Dispose() }
# open flyout via strip body click
Click 2100 1416; Start-Sleep -Milliseconds 800
# hover over track info area of flyout (flyout right edge ~2299, top ~1234; title area ~2100,1260)
[void][WI]::SetCursorPos(2100,1260); Start-Sleep -Milliseconds 700; Shot "flyout-track-hover"
# click track area -> Apple Music should come to foreground
Click 2100 1260; Start-Sleep -Milliseconds 1200
$fg = [WI]::GetForegroundWindow(); $sb = New-Object System.Text.StringBuilder 256; [void][WI]::GetClassName($fg,$sb,256)
"fg after track click: " + $sb
Shot "after-track-click"
