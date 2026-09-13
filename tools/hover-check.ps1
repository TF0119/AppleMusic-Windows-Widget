Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WH { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); }
"@
function Shot($n) { $b = New-Object System.Drawing.Bitmap 450,48; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(1900,1392,0,0,$b.Size); $g.Dispose(); $b.Save("$PSScriptRoot\out\$n.png"); $b.Dispose() }
[void][WH]::SetCursorPos(2100,1416); Start-Sleep -Milliseconds 700; Shot "hover-text"
[void][WH]::SetCursorPos(1980,1416); Start-Sleep -Milliseconds 700; Shot "hover-art"
[void][WH]::SetCursorPos(2270,1416); Start-Sleep -Milliseconds 700; Shot "hover-btn"
[void][WH]::SetCursorPos(1280,700)
