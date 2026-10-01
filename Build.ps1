param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts\AudioDeviceCmdlets.dll'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$automation = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll'
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $automation)) {
    throw 'This build requires Windows .NET Framework 4.x and Windows PowerShell 5.1.'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$null = New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath)
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'SOURCE') -Filter '*.cs' -Recurse | ForEach-Object FullName)
& $compiler /nologo /target:library /optimize+ /warn:4 "/out:$OutputPath" "/reference:$automation" @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output $OutputPath
