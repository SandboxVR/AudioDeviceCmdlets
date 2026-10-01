param(
    [string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot) 'artifacts\AudioDeviceCmdlets.dll'),
    [ValidateRange(100, 1000000)][int]$Iterations = 2000,
    [switch]$AllowLeakingBaseline
)

# Run in a fresh Windows PowerShell process for each assembly being compared.
# Only reads audio state; no volume/default-device changes are performed.
$ErrorActionPreference = 'Stop'
Import-Module $AssemblyPath

function Get-MemorySample {
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
    $process = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $process.Refresh()
        [pscustomobject]@{
            PrivateBytes = $process.PrivateMemorySize64
            ManagedBytes = [GC]::GetTotalMemory($true)
            Handles = $process.HandleCount
        }
    }
    finally { $process.Dispose() }
}

function New-VolumeWeakReference {
    $enumerator = New-Object CoreAudioApi.MMDeviceEnumerator
    $endpoint = $null
    try {
        $endpoint = $enumerator.GetDefaultAudioEndpoint([CoreAudioApi.EDataFlow]::eRender, [CoreAudioApi.ERole]::eMultimedia)
        $volume = $endpoint.AudioEndpointVolume
        $null = $volume.MasterVolumeLevelScalar
        New-Object WeakReference -ArgumentList $volume
    }
    finally {
        if ($endpoint -is [IDisposable]) { $endpoint.Dispose() }
        if ($enumerator -is [IDisposable]) { $enumerator.Dispose() }
    }
}

# Warm the JIT, native audio service and PowerShell command metadata first.
for ($i = 0; $i -lt 100; $i++) {
    $null = Get-AudioDevice -PlaybackVolume
    $null = Get-AudioDevice -PlaybackMute
    $null = Get-AudioDevice -List
}
$before = Get-MemorySample
for ($i = 0; $i -lt $Iterations; $i++) {
    $null = Get-AudioDevice -PlaybackVolume
    $null = Get-AudioDevice -PlaybackMute
    if ($i % 10 -eq 0) { $null = Get-AudioDevice -List }
}
$after = Get-MemorySample

$weakReferences = New-Object 'System.Collections.Generic.List[WeakReference]'
for ($i = 0; $i -lt 100; $i++) { $weakReferences.Add((New-VolumeWeakReference)) }
$null = Get-MemorySample
$retained = @($weakReferences | Where-Object IsAlive).Count
$report = [pscustomobject]@{
    Assembly = $AssemblyPath
    Iterations = $Iterations
    PrivateGrowthMiB = [Math]::Round(($after.PrivateBytes - $before.PrivateBytes) / 1MB, 2)
    ManagedGrowthMiB = [Math]::Round(($after.ManagedBytes - $before.ManagedBytes) / 1MB, 2)
    HandleGrowth = $after.Handles - $before.Handles
    RetainedVolumeWrappers = $retained
}
$report | ConvertTo-Json
if (!$AllowLeakingBaseline -and $retained -ne 0) { throw "$retained abandoned volume wrappers survived collection." }
if (!$AllowLeakingBaseline -and $after.PrivateBytes - $before.PrivateBytes -gt 32MB) {
    throw 'Private memory grew by more than 32 MiB after warmup; investigate on this host.'
}
