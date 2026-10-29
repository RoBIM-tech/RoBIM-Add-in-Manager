param([Parameter(Mandatory)][string]$HostZip,
      [Parameter(Mandatory)][string]$CadZip,
      [Parameter(Mandatory)][string]$OutdatedHostZip)
$ErrorActionPreference = 'Stop'
$check = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts/Test-BundledPackages.ps1'
& $check -RevitHostPackagePath $HostZip -CadPackagePath $CadZip
Write-Output 'PASS official pinned Host and CAD accepted'
$rejected = $false
try { & $check -RevitHostPackagePath $OutdatedHostZip -CadPackagePath $CadZip }
catch { if ($_.Exception.Message -notlike '*Fixed Revit Host must match official*') { throw }; $rejected = $true }
if (!$rejected) { throw 'An old Host with a valid local sidecar was accepted' }
Write-Output 'PASS outdated Host rejected despite matching local checksum sidecar'
