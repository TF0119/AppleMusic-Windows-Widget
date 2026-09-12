# Test event-based process detection (no elevation check) using notepad
$q = New-Object System.Management.WqlEventQuery "__InstanceCreationEvent", (New-Object TimeSpan 0,0,1), "TargetInstance isa 'Win32_Process' AND TargetInstance.Name='notepad.exe'"
$w = New-Object System.Management.ManagementEventWatcher $q
$w.Options.Timeout = New-Object TimeSpan 0,0,10
$w.Start()
Start-Sleep -Milliseconds 500
$p = Start-Process notepad -PassThru
try {
    $e = $w.WaitForNextEvent()
    if ($e) { Write-Host ("Creation event: " + $e.TargetInstance.Name + " pid=" + $e.TargetInstance.ProcessId) } else { Write-Host 'no creation event (timeout)' }
} catch { Write-Host "creation wait error: $_" }

$q2 = New-Object System.Management.WqlEventQuery "__InstanceDeletionEvent", (New-Object TimeSpan 0,0,1), "TargetInstance isa 'Win32_Process' AND TargetInstance.Name='notepad.exe'"
$w2 = New-Object System.Management.ManagementEventWatcher $q2
$w2.Options.Timeout = New-Object TimeSpan 0,0,10
$w2.Start()
Start-Sleep -Milliseconds 500
Stop-Process -Id $p.Id -Force
try {
    $e2 = $w2.WaitForNextEvent()
    if ($e2) { Write-Host ("Deletion event: " + $e2.TargetInstance.Name + " pid=" + $e2.TargetInstance.ProcessId) } else { Write-Host 'no deletion event (timeout)' }
} catch { Write-Host "deletion wait error: $_" }
$w.Stop(); $w2.Stop()
Write-Host "Elevated: $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
