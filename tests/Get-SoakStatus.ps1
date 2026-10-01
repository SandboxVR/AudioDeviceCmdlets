param([Parameter(Mandatory = $true)][string]$RunDirectory)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'manifest.json') | ConvertFrom-Json
$rows = foreach ($worker in $manifest.workers) {
    $process = Get-Process -Id $worker.pid -ErrorAction SilentlyContinue
    # ConvertFrom-Json may already deserialize an ISO timestamp to DateTime.
    # Parsing its culture-formatted string again would lose the UTC kind.
    $recordedStart = if ($worker.startedUtc -is [DateTime]) { $worker.startedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse($worker.startedUtc).UtcDateTime }
    $identityMatches = $null -ne $process -and [Math]::Abs(($process.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -lt 1
    $summaryPath = Join-Path $worker.directory 'summary.json'
    $summary = if (Test-Path -LiteralPath $summaryPath) { Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json } else { $null }
    $samplesPath = Join-Path $worker.directory 'samples.jsonl'
    $last = if (Test-Path -LiteralPath $samplesPath) { Get-Content -LiteralPath $samplesPath -Tail 1 } else { $null }
    $sample = if ($last) { $last | ConvertFrom-Json } else { $null }
    [pscustomobject]@{
        Variant = $worker.variant
        Workload = $worker.workload
        PID = $worker.pid
        Alive = $identityMatches
        State = if ($summary) { $summary.state } elseif ($identityMatches) { 'running' } else { 'missing' }
        Cycles = if ($sample) { $sample.cycles } else { 0 }
        PrivateMiB = if ($identityMatches) { [Math]::Round($process.PrivateMemorySize64 / 1MB, 2) } else { $null }
        Handles = if ($identityMatches) { $process.HandleCount } else { $null }
        LatestSampleUtc = if ($sample) { $sample.utc } else { $null }
        Directory = $worker.directory
    }
    if ($process) { $process.Dispose() }
}
$rows | ConvertTo-Json -Depth 4
