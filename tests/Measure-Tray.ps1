param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputPath,
    [int]$Samples = 10,
    [int]$SettleSeconds = 5
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (Get-Process BetterQuickLook -ErrorAction SilentlyContinue) { throw 'Exit BetterQuickLook before measuring a fresh tray instance.' }
$process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds $SettleSeconds
    $process.Refresh()
    if ($process.HasExited) { throw 'Application exited during startup.' }
    $cpuStart = $process.TotalProcessorTime.TotalMilliseconds
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $samplesRead = @()
    for ($i = 0; $i -lt $Samples; $i++) {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited while sampling.' }
        if ($process.MainWindowHandle -ne 0) { throw 'A preview opened during the idle sample. Repeat without pressing Space in Explorer.' }
        $elapsed = $clock.Elapsed.TotalSeconds
        $cpu = $process.TotalProcessorTime.TotalMilliseconds - $cpuStart
        $samplesRead += [pscustomobject]@{
            Seconds = $elapsed
            CpuMs = $cpu
            CpuPercentOneCore = $cpu / ($elapsed * 10)
            WorkingSetMiB = $process.WorkingSet64 / 1MB
            PrivateCommitMiB = $process.PrivateMemorySize64 / 1MB
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
        }
    }
    $report = [pscustomobject]@{
        Executable = $Executable
        Sha256 = (Get-FileHash -LiteralPath $Executable).Hash
        TimestampUtc = [DateTime]::UtcNow
        LogicalProcessors = [Environment]::ProcessorCount
        SettleSeconds = $SettleSeconds
        Samples = $samplesRead
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath
    $samplesRead | Select-Object -Last 1 | Format-List
} finally {
    $process.Refresh()
    if (!$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
    $process.Dispose()
}
