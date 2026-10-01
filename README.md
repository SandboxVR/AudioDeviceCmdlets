## Description
AudioDeviceCmdlets is a suite of PowerShell Cmdlets to control audio devices on Windows


## Features
Get list of all audio devices  
Get default audio device (playback/recording)  
Get default communication audio device (playback/recording)  
Get volume and mute state of default audio device (playback/recording)  
Get volume and mute state of default communication audio device (playback/recording)  
Set default audio device (playback/recording)  
Set default communication audio device (playback/recording)  
Set volume and mute state of default audio device (playback/recording)  
Set volume and mute state of default communication audio device (playback/recording)

Get and set volume and mute state of a selected playback or recording device by ID or index


## Installation
Run as administrator
```PowerShell
Install-Module -Name AudioDeviceCmdlets
```


## Usage
```PowerShell
Get-AudioDevice -ID <string>			# Get the device with the ID corresponding to the given <string>
Get-AudioDevice -Index <int>			# Get the device with the Index corresponding to the given <int>
Get-AudioDevice -List				# Get a list of all enabled devices as <AudioDevice>
Get-AudioDevice -PlaybackCommunication		# Get the default communication playback device as <AudioDevice>
Get-AudioDevice -PlaybackCommunicationMute	# Get the default communication playback device's mute state as <bool>
Get-AudioDevice -PlaybackCommunicationVolume	# Get the default communication playback device's volume level on 100 as <float>
Get-AudioDevice	-Playback			# Get the default playback device as <AudioDevice>
Get-AudioDevice -PlaybackMute			# Get the default playback device's mute state as <bool>
Get-AudioDevice -PlaybackVolume			# Get the default playback device's volume level on 100 as <float>
Get-AudioDevice -RecordingCommunication		# Get the default communication recording device as <AudioDevice>
Get-AudioDevice -RecordingCommunicationMute	# Get the default communication recording device's mute state as <bool>
Get-AudioDevice -RecordingCommunicationVolume	# Get the default communication recording device's volume level on 100 as <float>
Get-AudioDevice -Recording			# Get the default recording device as <AudioDevice>
Get-AudioDevice -RecordingMute			# Get the default recording device's mute state as <bool>
Get-AudioDevice -RecordingVolume		# Get the default recording device's volume level on 100 as <float>
```
```PowerShell
Set-AudioDevice	<AudioDevice>				# Set the given playback/recording device as both the default device and the default communication device, for its type
Set-AudioDevice <AudioDevice> -CommunicationOnly	# Set the given playback/recording device as the default communication device and not the default device, for its type
Set-AudioDevice <AudioDevice> -DefaultOnly		# Set the given playback/recording device as the default device and not the default communication device, for its type
Set-AudioDevice -ID <string>				# Set the device with the ID corresponding to the given <string> as both the default device and the default communication device, for its type
Set-AudioDevice -ID <string> -CommunicationOnly		# Set the device with the ID corresponding to the given <string> as the default communication device and not the default device, for its type
Set-AudioDevice -ID <string> -DefaultOnly		# Set the device with the ID corresponding to the given <string> as the default device and not the default communication device, for its type
Set-AudioDevice -Index <int>				# Set the device with the Index corresponding to the given <int> as both the default device and the default communication device, for its type
Set-AudioDevice -Index <int> -CommunicationOnly		# Set the device with the Index corresponding to the given <int> as the default communication device and not the default device, for its type
Set-AudioDevice -Index <int> -DefaultOnly		# Set the device with the Index corresponding to the given <int> as the default device and not the default communication device, for its type
Set-AudioDevice -PlaybackCommunicationMuteToggle	# Set the default communication playback device's mute state to the opposite of its current mute state
Set-AudioDevice -PlaybackCommunicationMute <bool>	# Set the default communication playback device's mute state to the given <bool>
Set-AudioDevice -PlaybackCommunicationVolume <float>	# Set the default communication playback device's volume level on 100 to the given <float>
Set-AudioDevice -PlaybackMuteToggle			# Set the default playback device's mute state to the opposite of its current mute state
Set-AudioDevice -PlaybackMute <bool>			# Set the default playback device's mute state to the given <bool>
Set-AudioDevice -PlaybackVolume <float>			# Set the default playback device's volume level on 100 to the given <float>
Set-AudioDevice -RecordingCommunicationMuteToggle	# Set the default communication recording device's mute state to the opposite of its current mute state
Set-AudioDevice -RecordingCommunicationMute <bool>	# Set the default communication recording device's mute state to the given <bool>
Set-AudioDevice -RecordingCommunicationVolume <float>	# Set the default communication recording device's volume level on 100 to the given <float>
Set-AudioDevice -RecordingMuteToggle			# Set the default recording device's mute state to the opposite of its current mute state
Set-AudioDevice -RecordingMute <bool>			# Set the default recording device's mute state to the given <bool>
Set-AudioDevice -RecordingVolume <float>		# Set the default recording device's volume level on 100 to the given <float>
```
```PowerShell
Write-AudioDevice -PlaybackCommunicationMeter	# Write the default playback device's power output on 100 as a meter
Write-AudioDevice -PlaybackCommunicationStream	# Write the default playback device's power output on 100 as a stream of <int>
Write-AudioDevice -PlaybackMeter		# Write the default playback device's power output on 100 as a meter
Write-AudioDevice -PlaybackStream		# Write the default playback device's power output on 100 as a stream of <int>
Write-AudioDevice -RecordingCommunicationMeter	# Write the default recording device's power output on 100 as a meter
Write-AudioDevice -RecordingCommunicationStream	# Write the default recording device's power output on 100 as a stream of <int>
Write-AudioDevice -RecordingMeter		# Write the default recording device's power output on 100 as a meter
Write-AudioDevice -RecordingStream		# Write the default recording device's power output on 100 as a stream of <int>
```


## Selected-device volume and mute

Use `-ID` or `-Index` with `-Volume` or `-Mute` to control any enabled playback
or recording endpoint, including a microphone or a device that is not the default.
Volume uses percentages from 0 to 100. The selected-device volume getter returns
a numeric `float`; the mute getter returns `bool`. Targeted setters produce no
output and do not change default-device assignments. Existing default-device
commands keep their behavior and output formats.

```PowerShell
Get-AudioDevice -ID $speakerId -Volume
Get-AudioDevice -ID $microphoneId -Mute
Set-AudioDevice -ID $speakerId -Volume 60
Set-AudioDevice -ID $microphoneId -Volume 100
Set-AudioDevice -ID $microphoneId -Mute $false
Set-AudioDevice -ID $speakerId -MuteToggle
Get-AudioDevice -Index 1 -Volume
Set-AudioDevice -Index 1 -Mute $false

$device = Get-AudioDevice -ID $microphoneId
try { $device | Set-AudioDevice -Volume 80 }
finally { $device.Dispose() }
```

Setters also accept `-InputObject`. They acquire their own temporary endpoint;
the caller retains ownership of the supplied `AudioDevice`. Volume, mute and
mute-toggle are separate operations. They cannot be combined with `-DefaultOnly`
or `-CommunicationOnly`. `Set-AudioDevice -ID $id` without a volume/mute option
continues to assign that device as the default.

For silica's configured adapters, whose `Volume` fields use the 0-to-1 scale,
the following function can run in the main service loop:

```PowerShell
function Restore-AudioDeviceSettings($adapters) {
    foreach ($adapter in $adapters) {
        $targetPercent = [single]($adapter.Volume * 100)
        $currentPercent = Get-AudioDevice -ID $adapter.ID -Volume
        if ([Math]::Abs($currentPercent - $targetPercent) -gt 0.1) {
            Set-AudioDevice -ID $adapter.ID -Volume $targetPercent
        }
        if (Get-AudioDevice -ID $adapter.ID -Mute) {
            Set-AudioDevice -ID $adapter.ID -Mute $false
        }
    }
}
```

This replaces the custom `AudioControl.AudioEndpointVolume` polling helper.
Calling it every two seconds preserves silica's existing enforcement cadence
without a separate volume-monitor runspace. It still needs periodic invocation
to restore settings changed by other applications. The helper's default-device
assignment can use the existing `Set-AudioDevice -ID` command; dispose its returned
`AudioDevice` as described below.

Verify the new API on a Windows host with active playback and recording endpoints:

```PowerShell
powershell.exe -NoProfile -File .\tests\Test-DeviceVolume.ps1
# Also exercise setters, briefly reducing volume and changing mute, with restoration:
powershell.exe -NoProfile -File .\tests\Test-DeviceVolume.ps1 -ExerciseWrites
```

## Build Cmdlet from source


<details>
  <summary>Build instructions</summary>

1. Install Visual Studio 2022

		Workloads: .NET desktop development

2. Create new project from SOURCE folder  
File -> New -> Project From Existing Code...

		Type of project: Visual C#
		Folder: SOURCE
		Name: AudioDeviceCmdlets
		Output type: Class Library

3. Set project properties  
Project -> AudioDeviceCmdlets Properties

		Assembly name: AudioDeviceCmdlets
		Target framework: .NET Framework 4.6.1+

4. Install System.Management.Automation NuGet legacy package  
Project -> Manage NuGet Packages...

		Package source: nuget.org
		Browse: Microsoft.PowerShell.5.1.ReferenceAssemblies
		Install: v1.0.0+

5. Set solution configuration  
Build -> Configuration Manager...

		Active solution configuration: Release

6. Build Cmdlet  
Build -> Build Solution

		AudioDeviceCmdlets\SOURCE\bin\Release\AudioDeviceCmdlets.dll

7. Import Cmdlet to PowerShell on Windows
	```PowerShell
	$FilePath = "C:\Path\To\AudioDeviceCmdlets\SOURCE\bin\Release\AudioDeviceCmdlets.dll"
	New-Item "$($profile | split-path)\Modules\AudioDeviceCmdlets" -Type directory -Force
	Copy-Item $FilePath "$($profile | split-path)\Modules\AudioDeviceCmdlets\AudioDeviceCmdlets.dll"
	Set-Location "$($profile | Split-Path)\Modules\AudioDeviceCmdlets"
	Get-ChildItem | Unblock-File
	Import-Module AudioDeviceCmdlets
	```
</details>


## Resource lifetimes in long-running jobs

Temporary devices, enumerators, collections, policy clients, meters and property
stores are disposed by the cmdlets, including failed operations and interrupted
meter/stream loops. Volume reads and writes do not register notification callbacks.
Subscribing to `OnVolumeNotification` registers one callback; removing the last
handler or disposing the volume unregisters it. The callback holds a weak reference
so an abandoned volume subscription can be finalized.

`Get-AudioDevice` and default-device setters return an `AudioDevice` with a usable
`Device`. The caller owns that returned object. Dispose it when finished if prompt
release is needed, especially when retaining devices in a service:

```PowerShell
$device = Get-AudioDevice -Playback
try {
    $device.Device.AudioEndpointVolume.MasterVolumeLevelScalar
}
finally {
    $device.Dispose()
}
```

CoreAudioApi wrappers also implement `IDisposable`. An `MMDevice` owns its cached
volume, meter, property store and session manager. A session manager owns its
session collection. Devices and sessions retrieved from collection indexers belong
to the caller. Session volume/meter interfaces borrow the session's COM reference;
disposing the session invalidates those interfaces and unregisters its event consumers.
Collections and enumerators do not dispose independently returned devices/sessions.

Property-store indexers copy values into managed snapshots and clear the native
`PROPVARIANT` immediately. `PropertyStore.GetValue(int)` returns a raw owning
`PropVariant`; callers must dispose it once and avoid disposing multiple struct
copies. Session strings and pinned channel buffers are freed in `finally` blocks.
Failed COM acquisitions and wrapper constructors also release their acquired
references. Releases balance one acquisition rather than forcibly releasing a
shared COM wrapper.

Native ownership follows Microsoft's documentation for
[PropVariantClear](https://learn.microsoft.com/en-us/windows/win32/api/propidl/nf-propidl-propvariantclear),
[volume callback registration](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-registercontrolchangenotify)
and [ReleaseComObject](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.releasecomobject).

Build and validate on Windows with .NET Framework 4.x and Windows PowerShell 5.1:

```PowerShell
.\Build.ps1
.\tests\Test-ResourceLifetimes.ps1
powershell.exe -NoProfile -File .\tests\Test-Cmdlets.ps1
powershell.exe -NoProfile -File .\tests\Test-LongRunning.ps1 -Iterations 2000
```

The DLL is written to `artifacts\AudioDeviceCmdlets.dll`. Resource tests cover x86
and x64, native property cleanup, callback retention, failing unregister/factory
operations, pinned-buffer failures and session ownership. Live tests require active
playback and recording defaults and only read audio state. Run the long-running test
in a fresh Windows PowerShell process; use `-AssemblyPath <original.dll>
-AllowLeakingBaseline` in a separate process to compare an older DLL. Its memory
threshold is a smoke check, not a substitute for a multi-day service soak.

For extended comparisons, build both frozen harnesses and start the background suite:

```PowerShell
.\tests\Build-SoakHarness.ps1
.\tests\Build-SoakHarness.ps1 -Baseline
.\tests\Start-ExtendedSoak.ps1
.\tests\Get-SoakStatus.ps1 -RunDirectory <printed-run-directory>
python .\tests\Analyze-Soak.py <printed-run-directory>
```

The extended suite runs 100,000 cmdlet cycles, 43,200 wrapper-churn cycles and
43,200 cached-endpoint service cycles against each build, plus a paced 24-hour
Windows PowerShell run that imports the patched DLL. Original-build stress runs
stop at 256 MiB private memory; patched runs stop at 512 MiB. These caps protect
the host during intentional leak reproduction. Workers write incremental memory,
handle and GC samples directly to disk, with fixed-size wrapper tracking. No
explicit GC is performed during measured loops; start/end GC snapshots are
labelled separately. All workloads only read audio state. A separate host monitor
records Windows audio-service memory and prevents automatic system sleep until
the workers exit; display sleep remains enabled. The launcher rejects duplicate
runs while any recorded worker is still active.

Accelerated cycles test allocation volume; the paced run tests elapsed time and
the actual PowerShell host. Neither recreates an unknown historic silica service
revision, its job-output buffers, registry mutations or hardware changes. Inspect
sampling gaps before interpreting a paced test as uninterrupted 24-hour coverage.


## Donation

<details>
  <summary>Thank you for considering a donation</summary>

	Bitcoin		(BTC) 3AffczXX4Jb2iN8QWQhHQAsj9AqGFXgYUF
	BitcoinCash	(BCH) qraf6a3fklta7xkvwkh49zqn6mgnm2eyz589rkfvl3
	Ethereum	(ETH) 0xE4EA2A2356C04c8054Db452dCBd6f958F74722dE
</details>


## Attribution

Based on code originally posted to Code Project by Ray Molenkamp with comments and suggestions by MadMidi  
http://www.codeproject.com/Articles/18520/Vista-Core-Audio-API-Master-Volume-Control  
Based on code originally posted to GitHub by Chris Hunt  
https://github.com/cdhunt/WindowsAudioDevice-Powershell-Cmdlet  
