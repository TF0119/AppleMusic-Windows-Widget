# Probe: taskbar geometry / alignment / auto-hide / secondary taskbars (for taskbar-docked widget feasibility)
Add-Type -Name U32 -Namespace Tb -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr FindWindow(string cls, string title);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr FindWindowEx(System.IntPtr parent, System.IntPtr after, string cls, string title);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr ctx);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetWindow(System.IntPtr h, uint cmd);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@
[Tb.U32]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Rect($h) { $r = New-Object 'Tb.U32+RECT'; [Tb.U32]::GetWindowRect($h, [ref]$r) | Out-Null; "$($r.Left),$($r.Top) -> $($r.Right),$($r.Bottom) ($($r.Right-$r.Left)x$($r.Bottom-$r.Top))" }
function Cls($h) { $sb = New-Object System.Text.StringBuilder 256; [Tb.U32]::GetClassName($h, $sb, 256) | Out-Null; $sb.ToString() }
function Children($parent, $indent) {
    $c = [Tb.U32]::GetWindow($parent, 5) # GW_CHILD
    while ($c -ne [IntPtr]::Zero) {
        "$indent$c $(Cls $c)  rect=$(Rect $c)"
        Children $c "$indent    "
        $c = [Tb.U32]::GetWindow($c, 2) # GW_HWNDNEXT
    }
}

$tray = [Tb.U32]::FindWindow("Shell_TrayWnd", $null)
"Shell_TrayWnd        : $tray  rect=$(Rect $tray)"
Children $tray "    "
$sec = [Tb.U32]::FindWindow("Shell_SecondaryTrayWnd", $null)
"Shell_SecondaryTrayWnd: $sec $(if ($sec -ne [IntPtr]::Zero) { 'rect=' + (Rect $sec) })"
if ($sec -ne [IntPtr]::Zero) { Children $sec "    " }

$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$p = Get-ItemProperty $adv -ErrorAction SilentlyContinue
"TaskbarAl (0=left,1=center,default=center): $($p.TaskbarAl)"
"TaskbarDa (widgets button): $($p.TaskbarDa)   TaskbarMn (chat): $($p.TaskbarMn)"
$ss = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3' -ErrorAction SilentlyContinue).Settings
if ($ss) { "AutoHide: $(($ss[8] -band 1) -eq 1)  Edge(0=L,1=T,2=R,3=B): $($ss[12])" }
"OS build: $([Environment]::OSVersion.Version)  $((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').DisplayVersion)"
"Theme: SystemUsesLightTheme=$((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize').SystemUsesLightTheme) AppsUseLightTheme=$((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize').AppsUseLightTheme)"
Add-Type -AssemblyName PresentationCore
"Segoe Fluent Icons installed: $([System.Windows.Media.Fonts]::SystemFontFamilies | Where-Object { $_.Source -eq 'Segoe Fluent Icons' } | ForEach-Object { 'yes' })"
