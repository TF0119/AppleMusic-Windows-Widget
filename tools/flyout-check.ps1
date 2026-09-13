# Phase 4 verification: click the strip body -> flyout opens above the taskbar;
# click an outside window -> closes; click strip again -> reopens (toggle).
# Checks use WindowFromPoint + GetClassName at the expected flyout center
# (our windows are HwndWrapper[AppleMusicWidget...]) and save PNGs to tools/out/.
param([int]$ProcessId, [string]$OutDir = "tools/out")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class FC {
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static string RootClassAt(int x, int y) {
    var h = GetAncestor(WindowFromPoint(new POINT{X=x,Y=y}), 2);
    var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString();
  }
  public static RECT Rect(IntPtr h) { var r = new RECT(); GetWindowRect(h, out r); return r; }
}
"@

[FC]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null # PER_MONITOR_AWARE_V2

function Click($x, $y) {
    [void][FC]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 80
    [FC]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [FC]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function OurWindows() {
    $list = New-Object 'System.Collections.Generic.List[IntPtr]'
    [FC]::EnumWindows({ param($h, $l)
        $p = 0; [FC]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
        if ($p -eq $ProcessId -and [FC]::IsWindowVisible($h)) { $list.Add($h) }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $list
}
function Shot($name, $x, $y, $w, $h) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $path = Join-Path $OutDir $name
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "saved $path"
}

# --- locate the strip (our only window before the flyout is created) ---
$wins = OurWindows
if ($wins.Count -eq 0) { "FAIL: no visible widget window"; exit 1 }
$strip = $wins | Where-Object { $r = [FC]::Rect($_); ($r.Bottom - $r.Top) -lt 100 } | Select-Object -First 1
if (-not $strip) { $strip = $wins[0] }
$sr = [FC]::Rect($strip)
$cx = [int](($sr.Left + $sr.Right) / 2); $cy = [int](($sr.Top + $sr.Bottom) / 2)
"strip: hwnd=$strip rect=$($sr.Left),$($sr.Top)-$($sr.Right),$($sr.Bottom) click=$cx,$cy"

# Region above the strip's right end, sized to cover a 350x150-dip flyout at up to 200% DPI.
$shotX = [Math]::Max(0, $sr.Right - 760); $shotY = [Math]::Max(0, $sr.Top - 340)
$shotW = [Math]::Min(770, 2560 - $shotX); $shotH = [Math]::Min(360, $sr.Bottom - $shotY)

function FlyoutHwnd() { OurWindows | Where-Object { $_ -ne $strip } | Select-Object -First 1 }
function Report($label) {
    $f = FlyoutHwnd
    if ($f) {
        $r = [FC]::Rect($f)
        $at = [FC]::RootClassAt([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2))
        "${label}: flyout hwnd=$f rect=$($r.Left),$($r.Top)-$($r.Right),$($r.Bottom) at-center='$at'"
    } else {
        # Probe the spot where the flyout would have been: 175dip/75dip up-left of the strip's right/top.
        $at = [FC]::RootClassAt($sr.Right - 260, $sr.Top - 120)
        "${label}: flyout closed (at-expected='$at')"
    }
}

Report "initial"

# 1) open
Click $cx $cy; Start-Sleep -Milliseconds 600
Report "after strip click"
Shot "flyout-1-open.png" $shotX $shotY $shotW $shotH | Out-Null

# 2) outside click -> close (foreground moves to whatever we click; neither our
#    window can take focus, so any other top-level works). Try candidates until closed.
$others = New-Object 'System.Collections.Generic.List[IntPtr]'
[FC]::EnumWindows({ param($h, $l)
    $p = 0; [FC]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
    if ($p -ne $ProcessId -and $p -ne $PID -and [FC]::IsWindowVisible($h)) {
        $r = [FC]::Rect($h)
        if (($r.Right - $r.Left) -gt 200 -and ($r.Bottom - $r.Top) -gt 200) { $others.Add($h) }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
$closed = $false
foreach ($h in $others) {
    $r = [FC]::Rect($h)
    Click ([int](($r.Left + $r.Right) / 2)) ([int](($r.Top + $r.Bottom) / 2))
    Start-Sleep -Milliseconds 500
    if (-not (FlyoutHwnd)) { $closed = $true; "outside click on hwnd=$h closed the flyout"; break }
}
if (-not $closed) {
    # fallback: bare desktop point far above the taskbar
    Click $cx ($sr.Top - 500); Start-Sleep -Milliseconds 500
    if (-not (FlyoutHwnd)) { $closed = $true; "desktop-point click closed the flyout" }
}
Report "after outside click"
Shot "flyout-2-outside.png" $shotX $shotY $shotW $shotH | Out-Null

# 3) toggle: strip click reopens
Click $cx $cy; Start-Sleep -Milliseconds 600
Report "toggle reopen"
Shot "flyout-3-toggle.png" $shotX $shotY $shotW $shotH | Out-Null

# 4) strip click again closes; leave the flyout closed
Click $cx $cy; Start-Sleep -Milliseconds 600
Report "toggle reclose"
Shot "flyout-4-reclosed.png" $shotX $shotY $shotW $shotH | Out-Null
