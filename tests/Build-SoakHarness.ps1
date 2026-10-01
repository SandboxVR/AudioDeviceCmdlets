param([switch]$Baseline)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$variant = if ($Baseline) { 'baseline' } else { 'patched' }
$directory = Join-Path $repoRoot "artifacts\soak\$variant"
$null = New-Item -ItemType Directory -Force -Path $directory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$automation = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll'
if ($Baseline) {
    $sourceRoot = Join-Path $directory 'SOURCE'
    $paths = @(git -C $repoRoot ls-tree -r --name-only HEAD -- SOURCE | Where-Object { $_ -like '*.cs' })
    foreach ($relative in $paths) {
        $destination = Join-Path $directory $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination)
        $source = git -C $repoRoot show "HEAD:$relative"
        if ($LASTEXITCODE -ne 0) { throw "Cannot export $relative" }
        [IO.File]::WriteAllText($destination, ($source -join "`r`n"))
    }
} else { $sourceRoot = Join-Path $repoRoot 'SOURCE' }
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse | ForEach-Object FullName)
$module = Join-Path $directory 'AudioDeviceCmdlets.dll'
& $compiler /nologo /target:library /optimize+ "/out:$module" "/reference:$automation" @sources
if ($LASTEXITCODE -ne 0) { throw 'Module compilation failed.' }
$runner = Join-Path $directory 'SoakHarness.exe'
& $compiler /nologo /target:exe /optimize+ "/out:$runner" "/reference:$automation" "/reference:$module" (Join-Path $PSScriptRoot 'SoakHarness.cs')
if ($LASTEXITCODE -ne 0) { throw 'Harness compilation failed.' }
[pscustomobject]@{
    Variant = $variant
    GitHead = (git -C $repoRoot rev-parse HEAD)
    ModuleHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $module).Hash
    HarnessHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $runner).Hash
    Sources = @($sources | ForEach-Object { Get-FileHash -Algorithm SHA256 -LiteralPath $_ | Select-Object Path, Hash })
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'build.json')
Write-Output $runner
