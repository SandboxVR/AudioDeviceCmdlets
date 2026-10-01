$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$automation = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'artifacts')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'SOURCE') -Filter '*.cs' -Recurse | ForEach-Object FullName)
$testSource = Join-Path $PSScriptRoot 'ResourceLifetimeTests.cs'
foreach ($platform in @('x64', 'x86')) {
    $output = Join-Path $repoRoot "artifacts\ResourceLifetimeTests-$platform.exe"
    & $compiler /nologo /target:exe /optimize+ "/platform:$platform" "/out:$output" "/reference:$automation" @sources $testSource
    if ($LASTEXITCODE -ne 0) { throw "Tests failed to compile ($platform)." }
    & $output
    if ($LASTEXITCODE -ne 0) { throw "Resource lifetime tests failed ($platform)." }
}
