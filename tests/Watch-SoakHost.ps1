param([Parameter(Mandatory = $true)][string]$RunDirectory, [switch]$KeepAwake)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'manifest.json') | ConvertFrom-Json
$writer = New-Object IO.StreamWriter (Join-Path $RunDirectory 'host-samples.jsonl')
$writer.AutoFlush = $true
if ($KeepAwake) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SoakExecutionState {
    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint flags);
}
'@
    # Prevent automatic system sleep while this test runs; allow display sleep.
    if ([SoakExecutionState]::SetThreadExecutionState([uint32]2147483649) -eq 0) { throw 'Cannot keep the soak host awake.' }
}
try {
    do {
        $alive = 0
        foreach ($worker in $manifest.workers) {
            $process = Get-Process -Id $worker.pid -ErrorAction SilentlyContinue
            if ($process) {
                $recordedStart = if ($worker.startedUtc -is [DateTime]) { $worker.startedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse($worker.startedUtc).UtcDateTime }
                if ([Math]::Abs(($process.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -lt 1) { $alive++ }
                $process.Dispose()
            }
        }
        $audioRows = @()
        $services = @(Get-CimInstance Win32_Service -Filter "Name='Audiosrv' OR Name='AudioEndpointBuilder'")
        $audioProcesses = @($services | Where-Object ProcessId -GT 0 | Select-Object -ExpandProperty ProcessId -Unique)
        $audioProcesses += @(Get-Process audiodg -ErrorAction SilentlyContinue | ForEach-Object { $id = $_.Id; $_.Dispose(); $id })
        foreach ($processId in ($audioProcesses | Select-Object -Unique)) {
            $audioProcess = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($audioProcess) {
                try {
                    $audioRows += [ordered]@{ pid = $audioProcess.Id; name = $audioProcess.ProcessName; privateBytes = $audioProcess.PrivateMemorySize64; workingSetBytes = $audioProcess.WorkingSet64; handles = $audioProcess.HandleCount }
                }
                catch { $audioRows += [ordered]@{ pid = $processId; unavailable = $_.Exception.Message } }
                finally { $audioProcess.Dispose() }
            }
        }
        $os = Get-CimInstance Win32_OperatingSystem
        $writer.WriteLine(([ordered]@{
            utc = [DateTime]::UtcNow.ToString('o')
            aliveWorkers = $alive
            freePhysicalBytes = [long]$os.FreePhysicalMemory * 1024
            totalPhysicalBytes = [long]$os.TotalVisibleMemorySize * 1024
            audioProcesses = $audioRows
        } | ConvertTo-Json -Depth 5 -Compress))
        if ($alive -gt 0) { Start-Sleep -Seconds 60 }
    } while ($alive -gt 0)
}
finally {
    if ($KeepAwake) { $null = [SoakExecutionState]::SetThreadExecutionState([uint32]2147483648) }
    $writer.Dispose()
}
