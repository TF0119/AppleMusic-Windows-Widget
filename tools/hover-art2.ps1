Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WJ { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); }
"@
function Shot($n) { $b = New-Object System.Drawing.Bitmap 450,48; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(1900,1392,0,0,$b.Size); $g.Dispose(); $b.Save("$PSScriptRoot\out\$n.png"); $b.Dispose() }
foreach ($x in 1970, 1981, 1995, 2005) {
  [void][WJ]::SetCursorPos($x,1416); Start-Sleep -Milliseconds 900; Shot "ha-$x"
}
[void][WJ]::SetCursorPos(1280,700)
