param([int]$ProcessId, [int]$Seconds = 10, [string]$Label = "")
$p = Get-Process -Id $ProcessId
$cpu0 = $p.TotalProcessorTime
$t0 = Get-Date
Start-Sleep -Seconds $Seconds
$p.Refresh()
$cpu1 = $p.TotalProcessorTime
$elapsed = ((Get-Date) - $t0).TotalMilliseconds
$pct = ($cpu1 - $cpu0).TotalMilliseconds / ($elapsed * [Environment]::ProcessorCount) * 100
"{0} cpu={1:F3}%  ws={2:F1}MB  cores={3}  (cpu delta {4:F0}ms over {5:F0}ms)" -f $Label, $pct, ($p.WorkingSet64 / 1MB), [Environment]::ProcessorCount, ($cpu1 - $cpu0).TotalMilliseconds, $elapsed
