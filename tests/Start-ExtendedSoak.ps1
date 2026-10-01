$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$soakRoot = Join-Path $repoRoot 'artifacts\soak'
if (Test-Path -LiteralPath $soakRoot) {
    foreach ($existingManifest in (Get-ChildItem -LiteralPath $soakRoot -Filter 'manifest.json' -Recurse)) {
        $existing = Get-Content -Raw -LiteralPath $existingManifest.FullName | ConvertFrom-Json
        foreach ($worker in $existing.workers) {
            $existingProcess = Get-Process -Id $worker.pid -ErrorAction SilentlyContinue
            if ($existingProcess) {
                try {
                    $recordedStart = if ($worker.startedUtc -is [DateTime]) { $worker.startedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse($worker.startedUtc).UtcDateTime }
                    if ([Math]::Abs(($existingProcess.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -lt 1) {
                        throw "An extended soak is already running: $($existingManifest.DirectoryName)"
                    }
                }
                finally { $existingProcess.Dispose() }
            }
        }
    }
}
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot = Join-Path $repoRoot "artifacts\soak\$stamp"
$null = New-Item -ItemType Directory -Force -Path $runRoot
$workers = New-Object 'System.Collections.Generic.List[object]'

function Start-Worker([string]$Variant, [string]$Workload, [long]$Cycles, [int]$SampleEvery, [int]$LimitMiB) {
    $directory = Join-Path $runRoot "$Variant-$Workload"
    $null = New-Item -ItemType Directory -Force -Path $directory
    $runner = Join-Path $repoRoot "artifacts\soak\$Variant\SoakHarness.exe"
    $arguments = @($Workload, "$Cycles", "$SampleEvery", '0', '0', "$LimitMiB", ('"' + $directory + '"'))
    $process = Start-Process -FilePath $runner -ArgumentList $arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $directory 'stdout.log') -RedirectStandardError (Join-Path $directory 'stderr.log')
    $workers.Add([pscustomobject]@{ variant = $Variant; workload = $Workload; pid = $process.Id; startedUtc = $process.StartTime.ToUniversalTime().ToString('o'); targetCycles = $Cycles; directory = $directory; privateLimitMiB = $LimitMiB })
    $process.Dispose()
}

# Baseline is a positive control; keep its intentionally leaking runs bounded.
Start-Worker baseline cmdlets 100000 500 256
Start-Worker baseline churn 43200 250 256
Start-Worker baseline service 43200 500 256
Start-Worker patched cmdlets 100000 1000 512
Start-Worker patched churn 43200 500 512
Start-Worker patched service 43200 500 512

$pacedDirectory = Join-Path $runRoot 'patched-powershell-24h'
$null = New-Item -ItemType Directory -Force -Path $pacedDirectory
$script = Join-Path $PSScriptRoot 'Test-ServiceSoak.ps1'
$module = Join-Path $repoRoot 'artifacts\soak\patched\AudioDeviceCmdlets.dll'
$arguments = @('-NoProfile', '-NonInteractive', '-File', ('"' + $script + '"'), '-AssemblyPath', ('"' + $module + '"'),
    '-OutputDirectory', ('"' + $pacedDirectory + '"'), '-DurationHours', '24', '-DelayMilliseconds', '2000', '-PrivateLimitMiB', '512')
$paced = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments `
    -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $pacedDirectory 'stdout.log') `
    -RedirectStandardError (Join-Path $pacedDirectory 'stderr.log')
$workers.Add([pscustomobject]@{ variant = 'patched'; workload = 'powershell-service'; pid = $paced.Id; startedUtc = $paced.StartTime.ToUniversalTime().ToString('o'); targetHours = 24; directory = $pacedDirectory; privateLimitMiB = 512 })
$paced.Dispose()

[ordered]@{
    runRoot = $runRoot
    startedUtc = [DateTime]::UtcNow.ToString('o')
    gitHead = (git -C $repoRoot rev-parse HEAD)
    patchedBuild = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'artifacts\soak\patched\build.json') | ConvertFrom-Json
    baselineBuild = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'artifacts\soak\baseline\build.json') | ConvertFrom-Json
    workers = $workers.ToArray()
    notes = 'Read-only stress tests. Accelerated cycle counts do not replace the paced 24-hour host test. No forced GC during measured loops. Baseline memory-limit termination is expected.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'manifest.json')
$watchScript = Join-Path $PSScriptRoot 'Watch-SoakHost.ps1'
$watchArguments = @('-NoProfile', '-NonInteractive', '-File', ('"' + $watchScript + '"'), '-RunDirectory', ('"' + $runRoot + '"'), '-KeepAwake')
$watcher = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $watchArguments `
    -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'host-stdout.log') `
    -RedirectStandardError (Join-Path $runRoot 'host-stderr.log')
[pscustomobject]@{ pid = $watcher.Id; startedUtc = $watcher.StartTime.ToUniversalTime().ToString('o'); keepsSystemAwake = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'host-monitor.json')
$watcher.Dispose()
Write-Output $runRoot
Write-Output ($workers.ToArray() | ConvertTo-Json -Depth 3)
