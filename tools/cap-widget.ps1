param([int]$ProcessId, [string]$OutPath, [int]$Pad = 0)
Add-Type -AssemblyName System.Drawing
Add-Type -Name U32 -Namespace Cap -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, System.IntPtr l);
public delegate bool EnumWindowsProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr ctx);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

[Cap.U32]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null # PER_MONITOR_AWARE_V2
$script:hwnd = [IntPtr]::Zero
[Cap.U32]::EnumWindows({
    param($h, $l)
    $pidOut = 0
    [Cap.U32]::GetWindowThreadProcessId($h, [ref]$pidOut) | Out-Null
    if ($pidOut -eq $ProcessId -and [Cap.U32]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null

if ($script:hwnd -eq [IntPtr]::Zero) { "no visible window"; exit 1 }

# raise to topmost temporarily so nothing occludes it
[Cap.U32]::SetWindowPos($script:hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null
Start-Sleep -Milliseconds 400

$r = New-Object 'Cap.U32+RECT'
[Cap.U32]::GetWindowRect($script:hwnd, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$x = $r.Left - $Pad; $y = $r.Top - $Pad
"hwnd=$($script:hwnd) rect=$($r.Left),$($r.Top) ${w}x${h}"

New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($w + 2 * $Pad), ($h + 2 * $Pad)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"saved $OutPath"
