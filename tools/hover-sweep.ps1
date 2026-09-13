Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WJ2 { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); }
"@
function Shot($n) { $b = New-Object System.Drawing.Bitmap 450,48; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(1900,1392,0,0,$b.Size); $g.Dispose(); $b.Save("$PSScriptRoot\out\$n.png"); $b.Dispose() }
foreach ($x in 1985, 2020, 2060, 2110) {
  [void][WJ2]::SetCursorPos($x,1416); Start-Sleep -Milliseconds 800; Shot "sweep-$x"
}
[void][WJ2]::SetCursorPos(1280,700)
