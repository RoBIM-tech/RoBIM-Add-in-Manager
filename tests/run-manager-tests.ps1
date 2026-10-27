param([switch]$Online)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$runRoot = Join-Path $root ('artifacts\manager-tests-' + [guid]::NewGuid().ToString('N'))
$arguments = @('run','--project',(Join-Path $root 'Manager.Tests\Manager.Tests.csproj'),'-c','Release','--',$runRoot)
if ($Online) { $arguments += '--online' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Manager tests failed: $runRoot" }
Copy-Item -LiteralPath (Join-Path $runRoot 'results.json') -Destination (Join-Path $root 'artifacts\manager-test-results.json') -Force

& python (Join-Path $PSScriptRoot 'test-ribbon-activation.py')
if ($LASTEXITCODE -ne 0) { throw 'Ribbon activation regression failed.' }

& python (Join-Path $root 'scripts/validate_registration.py')
if ($LASTEXITCODE -ne 0) { throw 'Central catalog regression failed.' }

& python (Join-Path $PSScriptRoot 'test-ribbon-retirement.py')
if ($LASTEXITCODE -ne 0) { throw 'Removed pyRevit ribbon regression failed.' }
