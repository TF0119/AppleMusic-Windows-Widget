param([int]$ProcessId, [int]$Samples = 1, [int]$IntervalMs = 600)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -Name U32 -Namespace Cap -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, System.IntPtr l);
public delegate bool EnumWindowsProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
'@
$script:hwnd = [IntPtr]::Zero
[Cap.U32]::EnumWindows({
    param($h, $l)
    $pidOut = 0
    [Cap.U32]::GetWindowThreadProcessId($h, [ref]$pidOut) | Out-Null
    if ($pidOut -eq $ProcessId -and [Cap.U32]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($script:hwnd -eq [IntPtr]::Zero) { "no visible window"; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($script:hwnd)
for ($i = 0; $i -lt $Samples; $i++) {
    $texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $names = ($texts | ForEach-Object { $_.Current.Name }) -join ' | '
    "{0} {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $names
    if ($i -lt $Samples - 1) { Start-Sleep -Milliseconds $IntervalMs }
}
