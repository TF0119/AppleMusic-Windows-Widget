# Snapshot memory of the widget process + module attribution.
# Usage: measure-mem.ps1 -Label "release-idle" [-ProcessName AppleMusicWidget] [-Modules]
# Appends one CSV line to tools/out/mem.csv: timestamp,label,pid,ws_mb,private_mb,cpu_pct_10s
param(
    [Parameter(Mandatory)][string]$Label,
    [string]$ProcessName = "AppleMusicWidget",
    [switch]$Modules,
    [int]$CpuWindowSec = 10
)
$p = Get-Process $ProcessName -ErrorAction Stop | Select-Object -First 1
$c1 = $p.TotalProcessorTime
Start-Sleep -Seconds $CpuWindowSec
$p.Refresh()
$c2 = $p.TotalProcessorTime
$cpu = [math]::Round(($c2 - $c1).TotalSeconds / $CpuWindowSec / [Environment]::ProcessorCount * 100, 4)
$ws = [math]::Round($p.WorkingSet64 / 1MB, 1)
$priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
$line = "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5}" -f (Get-Date), $Label, $p.Id, $ws, $priv, $cpu
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot "out") | Out-Null
Add-Content (Join-Path $PSScriptRoot "out\mem.csv") $line
Write-Host "label=$Label pid=$($p.Id) ws=${ws}MB private=${priv}MB cpu=${cpu}%"

if ($Modules) {
    Write-Host "--- managed/framework modules by size (top 25) ---"
    $p.Modules |
        Where-Object { $_.FileName -match 'dotnet\\shared|AppleMusicWidget|\\NuGet\\' } |
        Sort-Object ModuleMemorySize -Descending |
        Select-Object -First 25 @{n='MB';e={[math]::Round($_.ModuleMemorySize/1MB,2)}}, ModuleName |
        Format-Table -AutoSize
    Write-Host ("WinForms loaded: " + [bool]($p.Modules | Where-Object { $_.ModuleName -eq 'System.Windows.Forms.dll' }))
    Write-Host ("System.Drawing.Common loaded: " + [bool]($p.Modules | Where-Object { $_.ModuleName -eq 'System.Drawing.Common.dll' }))
    Write-Host ("Fluent theme loaded: " + [bool]($p.Modules | Where-Object { $_.ModuleName -eq 'PresentationFramework.Fluent.dll' }))
    Write-Host ("ICU loaded: " + [bool]($p.Modules | Where-Object { $_.ModuleName -like 'icu*.dll' }))
    Write-Host ("Module count: " + $p.Modules.Count)
}
