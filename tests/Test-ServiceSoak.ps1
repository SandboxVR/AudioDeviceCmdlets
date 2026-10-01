param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [double]$DurationHours = 24,
    [int]$DelayMilliseconds = 2000,
    [int]$PrivateLimitMiB = 512
)

# Actual Windows PowerShell host, imported binary module and the service cadence.
# Reads audio state only; never changes defaults, volumes or registry settings.
$ErrorActionPreference = 'Stop'
Import-Module $AssemblyPath
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$writer = New-Object IO.StreamWriter (Join-Path $OutputDirectory 'samples.jsonl')
$writer.AutoFlush = $true
$process = [Diagnostics.Process]::GetCurrentProcess()
$clock = [Diagnostics.Stopwatch]::StartNew()
$cycles = 0L
$commands = 0L
$reads = 0L
$devices = @()
$state = 'running'
$errorText = $null

function Write-Sample([string]$Phase) {
    $process.Refresh()
    $sample = [ordered]@{
        utc = [DateTime]::UtcNow.ToString('o')
        phase = $Phase
        workload = 'powershell-service'
        pid = $PID
        cycles = $cycles
        commands = $commands
        reads = $reads
        elapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
        privateBytes = $process.PrivateMemorySize64
        workingSetBytes = $process.WorkingSet64
        managedBytes = [GC]::GetTotalMemory($false)
        handles = $process.HandleCount
        gen0 = [GC]::CollectionCount(0)
        gen1 = [GC]::CollectionCount(1)
        gen2 = [GC]::CollectionCount(2)
    }
    $writer.WriteLine(($sample | ConvertTo-Json -Compress))
}

try {
    for ($i = 0; $i -lt 100; $i++) {
        $null = Get-AudioDevice -PlaybackVolume
        $null = Get-AudioDevice -RecordingMute
    }
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    $clock.Restart()
    Write-Sample 'start-after-gc'
    $lastSample = 0.0
    while ($clock.Elapsed.TotalHours -lt $DurationHours) {
        if ($cycles % 2000 -eq 0) {
            foreach ($device in $devices) { if ($device -is [IDisposable]) { $device.Dispose() } }
            $allDevices = @(Get-AudioDevice -List)
            $devices = @($allDevices | Select-Object -First 3)
            foreach ($device in ($allDevices | Select-Object -Skip 3)) {
                if ($device -is [IDisposable]) { $device.Dispose() }
            }
            $allDevices = $null
            $commands++
        }
        foreach ($device in $devices) {
            $volume = $device.Device.AudioEndpointVolume
            $null = $volume.MasterVolumeLevelScalar
            $null = $volume.Mute
            $reads += 2
        }
        if ($cycles % 5 -eq 0) {
            # Discard returned objects as older consumers did, allowing natural GC.
            $null = Get-AudioDevice -Playback
            $null = Get-AudioDevice -Recording
            $commands += 2
        }
        $cycles++
        if ($clock.Elapsed.TotalSeconds - $lastSample -ge 60) {
            Write-Sample 'running'
            $lastSample = $clock.Elapsed.TotalSeconds
            if ($process.PrivateMemorySize64 -ge $PrivateLimitMiB * 1MB) {
                $state = 'memory-limit'
                break
            }
        }
        if ($DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
    }
    Write-Sample 'end-before-gc'
    foreach ($device in $devices) { if ($device -is [IDisposable]) { $device.Dispose() } }
    $devices = @(); $device = $null; $volume = $null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    Write-Sample 'end-after-gc'
    if ($state -eq 'running') { $state = 'completed' }
}
catch {
    $state = 'failed'
    $errorText = $_.ToString() + "`n" + $_.ScriptStackTrace
    Write-Sample 'failed'
}
finally {
    $writer.Dispose()
    $process.Dispose()
    [ordered]@{
        state = $state
        workload = 'powershell-service'
        cycles = $cycles
        commands = $commands
        reads = $reads
        elapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
        durationHours = $DurationHours
        error = $errorText
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
}
if ($state -ne 'completed') { exit 1 }
