param(
    [string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot) 'artifacts\AudioDeviceCmdlets.dll'),
    [switch]$ExerciseWrites,
    [ValidateRange(0, 100000)][int]$PollIterations = 200
)

$ErrorActionPreference = 'Stop'
Import-Module ([IO.Path]::GetFullPath($AssemblyPath))

function Assert-Throws([scriptblock]$Action) {
    try { & $Action }
    catch { return }
    throw 'Expected the command to fail.'
}

function Get-DefaultIds {
    foreach ($selector in @('Playback', 'PlaybackCommunication', 'Recording', 'RecordingCommunication')) {
        $arguments = @{}; $arguments[$selector] = $true
        $endpoint = Get-AudioDevice @arguments
        try { "$selector=$($endpoint.ID)" }
        finally { $endpoint.Dispose() }
    }
}

$beforeDefaults = @(Get-DefaultIds)
$devices = @(Get-AudioDevice -List)
try {
    if (!($devices | Where-Object Type -EQ Playback) -or !($devices | Where-Object Type -EQ Recording)) {
        throw 'Live tests require both active playback and recording endpoints.'
    }
    foreach ($device in $devices) {
        $originalVolume = Get-AudioDevice -ID $device.ID -Volume
        $originalMute = Get-AudioDevice -ID $device.ID -Mute
        if ($originalVolume -isnot [single] -or $originalVolume -lt 0 -or $originalVolume -gt 100) {
            throw 'Selected-endpoint volume must be a numeric percentage.'
        }
        if ($originalMute -isnot [bool]) { throw 'Selected-endpoint mute must be boolean.' }
        if ([Math]::Abs((Get-AudioDevice -Index $device.Index -Volume) - $originalVolume) -gt 0.01 -or
            (Get-AudioDevice -Index $device.Index -Mute) -ne $originalMute) {
            throw 'ID and index getters disagree.'
        }
        if ($ExerciseWrites) {
            # Only decrease volume during the probe; restore both settings even on failure.
            $probeVolume = [single][Math]::Max(0, $originalVolume - 1)
            try {
                $output = @(Set-AudioDevice -ID $device.ID -Volume $probeVolume)
                if ($output.Count -ne 0) { throw 'A targeted setter returned an owning device.' }
                if ([Math]::Abs((Get-AudioDevice -ID $device.ID -Volume) - $probeVolume) -gt 0.1) {
                    throw 'ID setter did not change the selected endpoint volume.'
                }
                if ([Math]::Abs($device.Device.AudioEndpointVolume.MasterVolumeLevelScalar * 100 - $probeVolume) -gt 0.1) {
                    throw 'The caller-owned endpoint was invalidated or a different endpoint was changed.'
                }
                Set-AudioDevice -Index $device.Index -Volume $originalVolume
                $device | Set-AudioDevice -Volume $probeVolume
                if ([Math]::Abs((Get-AudioDevice -ID $device.ID -Volume) - $probeVolume) -gt 0.1) {
                    throw 'Pipeline volume setter failed.'
                }
                Set-AudioDevice -InputObject $device -Volume $originalVolume

                Set-AudioDevice -ID $device.ID -Mute $true
                if (!(Get-AudioDevice -ID $device.ID -Mute)) { throw 'ID mute setter failed.' }
                Set-AudioDevice -Index $device.Index -Mute $false
                if (Get-AudioDevice -ID $device.ID -Mute) { throw 'Index unmute setter failed.' }
                $device | Set-AudioDevice -Mute $true
                Set-AudioDevice -ID $device.ID -MuteToggle
                if (Get-AudioDevice -ID $device.ID -Mute) { throw 'ID mute toggle failed.' }
                Set-AudioDevice -Index $device.Index -MuteToggle
                if (!(Get-AudioDevice -ID $device.ID -Mute)) { throw 'Index mute toggle failed.' }
                $device | Set-AudioDevice -MuteToggle
                if (Get-AudioDevice -ID $device.ID -Mute) { throw 'Pipeline mute toggle failed.' }
                Set-AudioDevice -ID $device.ID -MuteToggle:$false
                if (Get-AudioDevice -ID $device.ID -Mute) { throw 'False toggle changed mute state.' }
            }
            finally {
                Set-AudioDevice -ID $device.ID -Volume $originalVolume
                Set-AudioDevice -ID $device.ID -Mute $originalMute
            }
        }
        Write-Output "PASS $($device.Type) $($device.Name) (default=$($device.Default))"
    }

    $validId = $devices[0].ID
    $missingIndex = $devices.Count + 1
    Assert-Throws { Get-AudioDevice -ID 'missing-test-device' -Volume }
    Assert-Throws { Get-AudioDevice -ID 'missing-test-device' -Mute }
    Assert-Throws { Set-AudioDevice -ID 'missing-test-device' -Volume 25 }
    Assert-Throws { Set-AudioDevice -ID 'missing-test-device' -Mute $false }
    Assert-Throws { Set-AudioDevice -ID 'missing-test-device' -MuteToggle }
    Assert-Throws { Get-AudioDevice -Index $missingIndex -Volume }
    Assert-Throws { Set-AudioDevice -Index $missingIndex -Mute $true }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume -1 }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume 101 }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume ([single]::NaN) }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume ([single]::PositiveInfinity) }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume $null }
    Assert-Throws { Set-AudioDevice -ID $validId -Mute $null }
    Assert-Throws { Get-AudioDevice -ID $validId -Volume -Mute }
    Assert-Throws { Set-AudioDevice -ID $validId -Volume 25 -DefaultOnly }
    Assert-Throws { Set-AudioDevice -ID $validId -Mute $true -CommunicationOnly }
    Assert-Throws { Set-AudioDevice -ID $validId -Mute $true -MuteToggle }
    Assert-Throws { Get-AudioDevice -ID $validId -Index 1 -Volume }

    # Repeated scalar reads own and release every temporary endpoint; no forced GC in the loop.
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    $process = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $process.Refresh()
        $initialPrivate = $process.PrivateMemorySize64
        $initialHandles = $process.HandleCount
        for ($i = 0; $i -lt $PollIterations; $i++) {
            foreach ($device in $devices) {
                $null = Get-AudioDevice -ID $device.ID -Volume
                $null = Get-AudioDevice -ID $device.ID -Mute
            }
        }
        [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
        $process.Refresh()
        $privateGrowth = $process.PrivateMemorySize64 - $initialPrivate
        $handleGrowth = $process.HandleCount - $initialHandles
        if ($privateGrowth -gt 32MB -or $handleGrowth -gt 100) {
            throw "Repeated targeted reads grew by $privateGrowth private bytes and $handleGrowth handles."
        }
        Write-Output "PASS $($PollIterations * $devices.Count * 2) targeted reads; private growth $([Math]::Round($privateGrowth / 1MB, 2)) MiB; handle growth $handleGrowth."
    }
    finally { $process.Dispose() }
    if ((@(Get-DefaultIds) -join '|') -ne ($beforeDefaults -join '|')) {
        throw 'Targeted volume/mute commands changed a default endpoint.'
    }
}
finally { foreach ($device in $devices) { $device.Dispose() } }
Write-Output 'PASS selected-device controls, invalid combinations, resource cleanup and unchanged defaults.'
