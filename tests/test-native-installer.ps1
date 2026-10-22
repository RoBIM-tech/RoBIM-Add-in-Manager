$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('artifacts\native-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$compiler = & (Join-Path $root 'installer\Get-InnoCompiler.ps1')
& $compiler '/DTestBuild' ('/O' + $testRoot) (Join-Path $root 'installer\Manager.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed' }
$setup = Join-Path $testRoot 'RoBIM-Manager-Setup.exe'
$destination = Join-Path $testRoot 'installed'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RoBIM.AddinManager.Test_is1'
if (Test-Path $key) { throw 'Another native installer test is registered; refusing to replace it' }
function Install-Test([string]$Target) {
    $p = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $Target + '"'),('/LOG="' + (Join-Path $testRoot 'install.log') + '"')) -PassThru -WindowStyle Hidden -Wait
    return $p.ExitCode
}
if ((Install-Test $destination) -ne 0) { throw 'Native install failed' }
$values = Get-ItemProperty -LiteralPath $key
if ($values.DisplayName -ne 'RoBIM Add-in Manager' -or $values.InstallLocation.TrimEnd('\') -ne $destination) { throw 'ARP registration incorrect' }
$uninstaller = Join-Path $destination 'unins000.exe'
if (!(Test-Path -LiteralPath $uninstaller) -or !$values.UninstallString.Contains('unins000.exe')) { throw 'Native uninstaller missing' }
$keep = Join-Path $destination 'personal.keep'
Set-Content -LiteralPath $keep -Value 'must survive'
if ((Install-Test $destination) -ne 0) { throw 'Native upgrade failed' }
$p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -WindowStyle Hidden -Wait
if ($p.ExitCode -ne 0) { throw 'Native uninstall failed' }
for ($i=0; $i -lt 40 -and (Test-Path -LiteralPath $key); $i++) { Start-Sleep -Milliseconds 250 }
if ((Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath (Join-Path $destination 'RoBIM.Manager.exe')) -or !(Test-Path -LiteralPath $keep)) { throw 'Native uninstall/preservation check failed' }
$external = Join-Path $testRoot 'unmanaged'
New-Item -ItemType Directory -Path $external | Out-Null
Set-Content -LiteralPath (Join-Path $external 'keep.txt') -Value 'unmanaged'
if ((Install-Test $external) -eq 0 -or (Test-Path -LiteralPath (Join-Path $external 'RoBIM.Manager.exe'))) { throw 'Unmanaged folder was overwritten' }
Write-Output "Native setup checks passed: install, ARP, native uninstaller, upgrade, uninstall, unknown-file preservation, unmanaged-folder guard. $testRoot"
