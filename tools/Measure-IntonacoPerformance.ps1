# Samples the running Intonaco process for repeatable before/after checks.
#
# Examples:
#   .\tools\Measure-IntonacoPerformance.ps1 -Label "Playing 1080p" -DurationSeconds 20
#   .\tools\Measure-IntonacoPerformance.ps1 -Label "Paused" -DurationSeconds 20 -OutputCsv .\paused.csv
#
# Windows does not expose one universally reliable per-process GPU counter
# name across GPU vendors/builds, so record Task Manager's GPU and Video
# Decode percentages alongside this RAM/CPU output.

[CmdletBinding()]
param(
    [string]$Label = "Intonaco",
    [int]$DurationSeconds = 20,
    [string]$OutputCsv
)

$ErrorActionPreference = "Stop"

if ($DurationSeconds -lt 2) {
    throw "DurationSeconds must be at least 2."
}

$process = Get-Process -Name "Intonaco" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $process) {
    throw "Intonaco is not running."
}

Write-Host "Sampling $Label (PID $($process.Id)) for $DurationSeconds seconds..."
$process.Refresh()
$previousCpu = $process.TotalProcessorTime
$previousTime = Get-Date
$samples = @()

for ($i = 0; $i -lt $DurationSeconds; $i++) {
    Start-Sleep -Seconds 1

    $process.Refresh()
    if ($process.HasExited) {
        throw "Intonaco exited while sampling."
    }

    $now = Get-Date
    $elapsed = ($now - $previousTime).TotalSeconds
    $cpuSeconds = ($process.TotalProcessorTime - $previousCpu).TotalSeconds
    $cpuPercent = 0.0
    if ($elapsed -gt 0) {
        $cpuPercent = 100.0 * $cpuSeconds / ($elapsed * [Environment]::ProcessorCount)
    }

    $samples += [pscustomobject]@{
        Label             = $Label
        Timestamp         = $now
        WorkingSetMB      = [math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMemoryMB   = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        CpuPercent        = [math]::Round($cpuPercent, 3)
        Threads           = $process.Threads.Count
        Handles           = $process.HandleCount
    }

    $previousCpu = $process.TotalProcessorTime
    $previousTime = $now
}

if ($OutputCsv) {
    $samples | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding utf8
    Write-Host "Samples written to $OutputCsv"
}

$working = $samples | Measure-Object WorkingSetMB -Average -Maximum
$private = $samples | Measure-Object PrivateMemoryMB -Average -Maximum
$cpu = $samples | Measure-Object CpuPercent -Average -Maximum

[pscustomobject]@{
    Label                  = $Label
    Samples                = $samples.Count
    AvgWorkingSetMB        = [math]::Round($working.Average, 2)
    MaxWorkingSetMB        = [math]::Round($working.Maximum, 2)
    AvgPrivateMemoryMB     = [math]::Round($private.Average, 2)
    MaxPrivateMemoryMB     = [math]::Round($private.Maximum, 2)
    AvgCpuPercent          = [math]::Round($cpu.Average, 3)
    MaxCpuPercent          = [math]::Round($cpu.Maximum, 3)
} | Format-List
