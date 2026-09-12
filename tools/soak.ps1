# Leak soak tests (PLAN Phase 3 計測). Samples WS/Private into tools/out/mem.csv via measure-mem.ps1.
# Modes:
#   skips   : skip N tracks via GsmtcCtl next, sample every K skips   (PLAN: 100曲連続切替)
#   cycles  : start/stop Apple Music N times, sample each cycle        (PLAN: 起動/終了 50回)
#   play    : leave playing for N minutes, sample every K minutes      (PLAN: 1時間連続再生)
param(
    [Parameter(Mandatory)][ValidateSet("skips", "cycles", "play")][string]$Mode,
    [int]$Count = 100,
    [int]$SampleEvery = 10,
    [double]$IntervalSec = 2,
    [string]$Label = ""
)
$ctl = Join-Path $PSScriptRoot "GsmtcCtl\bin\Debug\net10.0-windows10.0.19041.0\GsmtcCtl.exe"
if (-not (Test-Path $ctl)) { $ctl = Get-ChildItem (Join-Path $PSScriptRoot "GsmtcCtl\bin") -Recurse -Filter GsmtcCtl.exe | Select-Object -First 1 -ExpandProperty FullName }
$measure = Join-Path $PSScriptRoot "measure-mem.ps1"
$aumid = "shell:AppsFolder\AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"
if (-not $Label) { $Label = $Mode }

function Sample($tag) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $measure -Label "$Label-$tag" -CpuWindowSec 5 }

Sample "start"
switch ($Mode) {
    "skips" {
        for ($i = 1; $i -le $Count; $i++) {
            & $ctl next | Out-Null
            Start-Sleep -Seconds $IntervalSec
            if ($i % $SampleEvery -eq 0) { Sample "skip$i" }
        }
    }
    "cycles" {
        for ($i = 1; $i -le $Count; $i++) {
            $am = Get-Process AppleMusic -ErrorAction SilentlyContinue
            if ($am) { $am.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 6 }
            if (Get-Process AppleMusic -ErrorAction SilentlyContinue) { Stop-Process -Name AppleMusic -Force; Start-Sleep -Seconds 2 }
            Start-Process $aumid
            Start-Sleep -Seconds 10
            if ($i % $SampleEvery -eq 0) { Sample "cycle$i" }
        }
    }
    "play" {
        $minutes = $Count
        for ($m = $SampleEvery; $m -le $minutes; $m += $SampleEvery) {
            Start-Sleep -Seconds ($SampleEvery * 60)
            Sample "min$m"
        }
    }
}
Sample "end"
Write-Host "done. see tools/out/mem.csv"
