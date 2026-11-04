param([string]$PortablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\RoBIM-Manager-Portable.zip'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runRoot = [IO.Path]::GetFullPath((Join-Path $root ('artifacts\setup-tests-' + [guid]::NewGuid().ToString('N'))))
if (!$runRoot.StartsWith(([IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path outside workspace artifacts' }
New-Item -ItemType Directory -Path $runRoot | Out-Null
$extract = Join-Path $runRoot 'extracted'
Expand-Archive -LiteralPath $PortablePath -DestinationPath $extract
$payload = Join-Path $extract 'RoBIM-Manager'
$target = Join-Path $runRoot 'installed'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $payload 'Install.ps1') -Destination $target -Quiet -SkipIntegration
if ($LASTEXITCODE -ne 0) { throw 'Installer script failed' }
if (!(Test-Path -LiteralPath (Join-Path $target 'RoBIM.Manager.exe'))) { throw 'Manager EXE missing after install' }
foreach ($name in @('RoBIM-Host-Revit2026.zip','RoBIM-CAD-Loader-AutoCAD2026.zip')) {
    if (Test-Path -LiteralPath (Join-Path $target $name)) { throw ('Loader must not be bundled: ' + $name) }
}
if (!(Test-Path -LiteralPath (Join-Path $target 'YamlDotNet-LICENSE.txt'))) { throw 'Dependency license missing after install' }
if (Test-Path -LiteralPath (Join-Path $target 'RoBIM-ReloadBridge.zip')) { throw 'Retired pyRevit bridge unexpectedly bundled' }
$preserved = Join-Path $target 'personal-file.keep'
Set-Content -LiteralPath $preserved -Value 'must survive uninstall'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $payload 'Install.ps1') -Destination $target -Quiet -SkipIntegration
if ($LASTEXITCODE -ne 0) { throw 'Reinstall failed' }
$preview = Join-Path $runRoot 'preview.png'
$process = Start-Process -FilePath (Join-Path $target 'RoBIM.Manager.exe') -ArgumentList @('--render-preview',('"' + $preview + '"')) -PassThru -WindowStyle Hidden
if (!$process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force; throw 'Installed manager render timeout' }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $preview)) { throw 'Installed self-contained manager failed to render' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target 'Uninstall.ps1') -Quiet -KeepUserData
if ($LASTEXITCODE -ne 0) { throw 'Uninstall failed' }
if ((Test-Path -LiteralPath (Join-Path $target 'RoBIM.Manager.exe')) -or !(Test-Path -LiteralPath $preserved)) { throw 'Uninstall failed to preserve unknown file' }
$external = Join-Path $runRoot 'external'
New-Item -ItemType Directory -Path $external | Out-Null
Set-Content -LiteralPath (Join-Path $external 'keep.txt') -Value 'unmanaged'
$previousErrorPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue' # This child is expected to reject an unmanaged destination.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $payload 'Install.ps1') -Destination $external -Quiet -SkipIntegration 2>&1
} finally { $ErrorActionPreference = $previousErrorPreference }
if ($LASTEXITCODE -eq 0 -or !(Test-Path -LiteralPath (Join-Path $external 'keep.txt'))) { throw 'Unmanaged installer destination was not protected' }
@{
    PortableExtraction=$true; Installation=$true; Reinstallation=$true; InstalledUiRender=$true
    Uninstall=$true; UnknownFilePreserved=$true; UnmanagedDestinationProtected=$true; PyRevitExcluded=$true; NativeRevitHostPackage=$true; DependencyLicenses=$true
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts\setup-test-results.json') -Encoding UTF8
Copy-Item -LiteralPath $preview -Destination (Join-Path $root 'artifacts\manager-preview.png') -Force
Write-Output "Setup checks passed: $runRoot"
$global:LASTEXITCODE = 0 # The unmanaged-destination test above intentionally runs a failing child.
