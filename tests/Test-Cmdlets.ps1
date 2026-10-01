param([string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot) 'artifacts\AudioDeviceCmdlets.dll'))

$ErrorActionPreference = 'Stop'
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
Import-Module $AssemblyPath

function Assert-Throws([scriptblock]$Action) {
    try { & $Action }
    catch { return }
    throw 'Expected the command to fail.'
}

$devices = @(Get-AudioDevice -List)
if ($devices.Count -eq 0) { throw 'Live smoke tests require an active audio device.' }
foreach ($device in $devices) {
    try {
        $byID = Get-AudioDevice -ID $device.ID
        $byIndex = Get-AudioDevice -Index $device.Index
        try {
            if ($byID.ID -ne $device.ID -or $byIndex.ID -ne $device.ID) { throw 'Device lookup changed.' }
            # Other wrappers of the same native device remain usable when one is disposed.
            $byID.Dispose()
            $null = $byIndex.Device.ID
            $null = $device.Device.FriendlyName
        }
        finally { $byID.Dispose(); $byIndex.Dispose() }
    }
    finally { $device.Dispose() }
}
foreach ($parameter in @('Playback', 'PlaybackCommunication', 'Recording', 'RecordingCommunication')) {
    $arguments = @{}; $arguments[$parameter] = $true
    $device = Get-AudioDevice @arguments
    try {
        $null = $device.Device.AudioEndpointVolume.MasterVolumeLevelScalar
        $arguments = @{}; $arguments["${parameter}Mute"] = $true
        if ((Get-AudioDevice @arguments) -isnot [bool]) { throw 'Mute output changed.' }
        $arguments = @{}; $arguments["${parameter}Volume"] = $true
        if ((Get-AudioDevice @arguments) -notmatch '%$') { throw 'Volume output changed.' }
    }
    finally { $device.Dispose() }
}
Assert-Throws { Get-AudioDevice -ID 'missing-test-device' }
Assert-Throws { Set-AudioDevice -ID 'missing-test-device' }
Assert-Throws { Set-AudioDevice -ID 'missing-test-device' -DefaultOnly -CommunicationOnly }
foreach ($verb in @('Get', 'Set', 'Write')) {
    $null = & "$verb-AudioDevice" -Version
}

foreach ($parameter in @('PlaybackStream', 'PlaybackMeter', 'PlaybackCommunicationStream', 'PlaybackCommunicationMeter',
    'RecordingStream', 'RecordingMeter', 'RecordingCommunicationStream', 'RecordingCommunicationMeter')) {
    $pipeline = [PowerShell]::Create()
    $output = New-Object 'System.Management.Automation.PSDataCollection[psobject]'
    try {
        $null = $pipeline.AddCommand('Import-Module').AddParameter('Name', $AssemblyPath).AddStatement()
        $null = $pipeline.AddCommand('Write-AudioDevice').AddParameter($parameter)
        # Windows PowerShell 5.1 requires reflection for generic methods.
        $begin = [PowerShell].GetMethods() | Where-Object {
            $_.Name -eq 'BeginInvoke' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 2
        } | Select-Object -First 1
        $invokeArguments = New-Object object[] 2
        $invokeArguments[1] = $output.PSObject.BaseObject
        $pending = $begin.MakeGenericMethod([psobject], [psobject]).Invoke($pipeline, $invokeArguments)
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while ($output.Count -eq 0 -and $pipeline.Streams.Progress.Count -eq 0 -and
            !$pending.IsCompleted -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 50
        }
        $pipeline.Stop()
        try { $null = $pipeline.EndInvoke($pending) }
        catch [System.Management.Automation.PipelineStoppedException] { }
        $unexpected = @($pipeline.Streams.Error | Where-Object { $_.FullyQualifiedErrorId -ne 'PipelineStopped' })
        if ($unexpected.Count -ne 0) { throw $unexpected[0] }
        if ($output.Count -eq 0 -and $pipeline.Streams.Progress.Count -eq 0) { throw "$parameter produced no samples." }
    }
    finally { $output.Dispose(); $pipeline.Dispose() }
}
Write-Output 'PASS device lookups, all default getters, invalid inputs, all meter/stream cancellation paths.'
