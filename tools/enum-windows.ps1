# Enumerate visible windows owned by the AppleMusicWidget process
param([int]$ProcessId)

Add-Type -Name U32 -Namespace W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, System.IntPtr l);
public delegate bool EnumWindowsProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetWindowTextLength(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$found = $false
[W.U32]::EnumWindows({
    param($h, $l)
    $pidOut = 0
    [W.U32]::GetWindowThreadProcessId($h, [ref]$pidOut) | Out-Null
    if ($pidOut -eq $ProcessId) {
        $vis = [W.U32]::IsWindowVisible($h)
        $len = [W.U32]::GetWindowTextLength($h)
        $sb = New-Object System.Text.StringBuilder ($len + 1)
        [W.U32]::GetWindowText($h, $sb, $len + 1) | Out-Null
        $r = New-Object 'W.U32+RECT'
        [W.U32]::GetWindowRect($h, [ref]$r) | Out-Null
        Write-Host ("hwnd=$h visible=$vis title='$($sb.ToString())' rect=$($r.Left),$($r.Top)-$($r.Right),$($r.Bottom)")
        $script:found = $true
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
if (-not $found) { Write-Host "no windows found" }
