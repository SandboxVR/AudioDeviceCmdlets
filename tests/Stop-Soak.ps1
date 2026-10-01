param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [ValidateSet('baseline', 'patched', 'all')][string]$Variant = 'all',
    [string[]]$Workload = @(),
    [Parameter(Mandatory = $true)][string]$Reason
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'manifest.json') | ConvertFrom-Json
foreach ($worker in $manifest.workers) {
    if ($Variant -ne 'all' -and $worker.variant -ne $Variant) { continue }
    if ($Workload.Count -gt 0 -and $worker.workload -notin $Workload) { continue }
    $process = Get-Process -Id $worker.pid -ErrorAction SilentlyContinue
    if (!$process) { continue }
    try {
        $recordedStart = if ($worker.startedUtc -is [DateTime]) { $worker.startedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse($worker.startedUtc).UtcDateTime }
        if ([Math]::Abs(($process.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -ge 1) {
            throw "PID $($worker.pid) no longer belongs to this soak worker."
        }
        $process.Refresh()
        $snapshot = [ordered]@{
            stoppedUtc = [DateTime]::UtcNow.ToString('o')
            pid = $worker.pid
            privateBytes = $process.PrivateMemorySize64
            workingSetBytes = $process.WorkingSet64
            handles = $process.HandleCount
            reason = $Reason
        }
        Stop-Process -Id $worker.pid -Force
        $null = $process.WaitForExit(10000)
        $snapshot | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $worker.directory 'controller-stop.json')
        $summaryPath = Join-Path $worker.directory 'summary.json'
        if (!(Test-Path -LiteralPath $summaryPath)) {
            $last = Get-Content -LiteralPath (Join-Path $worker.directory 'samples.jsonl') -Tail 1 | ConvertFrom-Json
            [ordered]@{
                state = 'stopped-by-controller'
                workload = $worker.workload
                cycles = $last.cycles
                commands = $last.commands
                reads = $last.reads
                elapsedSeconds = $last.elapsedSeconds
                countsAreLastSampleLowerBounds = $true
                retainedVolumeWrappers = $null
                forcedGcAtStop = $false
                reason = $Reason
            } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
        }
        Write-Output "Stopped $($worker.variant) $($worker.workload), PID $($worker.pid)."
    }
    finally { $process.Dispose() }
}
