param([switch]$SkipCadBuild, [string]$CadPackagePath, [string]$RevitHostPackagePath, [string]$LibraryFeed)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
python (Join-Path $PSScriptRoot 'scripts/validate_registration.py')
if ($LASTEXITCODE -ne 0) { throw 'Central button catalog validation failed.' }
# Compatibility switch retained for existing build commands. CAD is a separately released package.
$cadPackage = if ($CadPackagePath) { [IO.Path]::GetFullPath($CadPackagePath) } else { Join-Path $PSScriptRoot 'artifacts\DelHiddenNet-AutoCAD2026.zip' }
if (!(Test-Path -LiteralPath $cadPackage) -or !(Test-Path -LiteralPath ($cadPackage + '.sha256'))) {
    if ($CadPackagePath) { throw 'Explicit CAD package and its .sha256 sidecar are required.' }
    & (Join-Path $PSScriptRoot 'fetch-cad-package.ps1')
}
if ((Get-FileHash -LiteralPath $cadPackage).Hash -ne (Get-Content -LiteralPath ($cadPackage + '.sha256') -Raw).Trim()) { throw 'Cached CAD release SHA-256 mismatch' }
$hostPackage = if ($RevitHostPackagePath) { [IO.Path]::GetFullPath($RevitHostPackagePath) } else { Join-Path $PSScriptRoot 'artifacts\RoBIM-Host-Revit2026.zip' }
if (!(Test-Path -LiteralPath $hostPackage) -or !(Test-Path -LiteralPath ($hostPackage + '.sha256')) -or (Get-FileHash -LiteralPath $hostPackage).Hash -ne (Get-Content -LiteralPath ($hostPackage + '.sha256') -Raw).Trim()) { throw 'A verified RoBIM-Host-Revit2026.zip release asset is required. Pass -RevitHostPackagePath; Manager never builds module repositories.' }
$output = Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager'
if (Test-Path -LiteralPath $output) {
    Get-ChildItem -LiteralPath $output -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}
$libraryArgs = @('--source', 'https://api.nuget.org/v3/index.json')
if ($LibraryFeed) {
    $libraryArgs = @('--source', [IO.Path]::GetFullPath($LibraryFeed), '--source', 'https://api.nuget.org/v3/index.json')
    foreach ($cache in @((Join-Path $env:USERPROFILE '.nuget/packages'), (Join-Path $PSScriptRoot '.dotnet-home/.nuget/packages'))) {
        if (Test-Path -LiteralPath $cache -PathType Container) { $libraryArgs += @('--source', $cache) }
    }
    $libraryArgs += @(('-p:RestorePackagesPath=' + (Join-Path $PSScriptRoot 'artifacts/access-packages')), '-p:NuGetAudit=false')
}
& dotnet publish (Join-Path $PSScriptRoot 'RoBIM.Manager\RoBIM.Manager.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true @libraryArgs -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Manager publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/licenses/YamlDotNet-LICENSE.txt') -Destination (Join-Path $output 'YamlDotNet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $output 'THIRD_PARTY_NOTICES.md') -Force
$runtimeConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RoBIM.Manager/bin/Release/net10.0-windows/win-x64/RoBIM.Manager.runtimeconfig.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RoBIM.Manager/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packageName = if ($framework.name -eq 'Microsoft.NETCore.App') { 'microsoft.netcore.app.runtime.win-x64' } else { 'microsoft.windowsdesktop.app.runtime.win-x64' }
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ ($packageName + '/' + $framework.version) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$packageRoot) { throw ('Runtime license package missing: ' + $packageName) }
    foreach ($license in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $licenseFile = Join-Path $packageRoot $license
        if (!(Test-Path -LiteralPath $licenseFile) -and $license -eq 'LICENSE.TXT') { $licenseFile = Join-Path $packageRoot 'LICENSE' }
        if (Test-Path -LiteralPath $licenseFile) { Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $output ($framework.name + '-' + $license)) -Force }
        elseif ($license -eq 'LICENSE.TXT') { throw ('Runtime license missing: ' + $framework.name) }
    }
}
$cad = $cadPackage
Copy-Item -LiteralPath $cad -Destination (Join-Path $output 'DelHiddenNet-AutoCAD2026.zip') -Force
Copy-Item -LiteralPath $hostPackage -Destination (Join-Path $output 'RoBIM-Host-Revit2026.zip') -Force
Copy-Item -LiteralPath ($hostPackage + '.sha256') -Destination (Join-Path $output 'RoBIM-Host-Revit2026.zip.sha256') -Force
$bridge = Join-Path $output 'RoBIM-ReloadBridge.zip'
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'reload-bridge\RoBIM Manager.extension') -DestinationPath $bridge -Force
(Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash | Set-Content -LiteralPath ($bridge + '.sha256') -Encoding ASCII
(Get-FileHash -LiteralPath $cad -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $output 'DelHiddenNet-AutoCAD2026.zip.sha256') -Encoding ASCII
foreach ($name in @('Install.ps1','Uninstall.ps1')) {
    $source = Join-Path $PSScriptRoot "installer\$name"
    [IO.File]::WriteAllText((Join-Path $output $name), [IO.File]::ReadAllText($source), [Text.UTF8Encoding]::new($true))
}
Compress-Archive -LiteralPath $output -DestinationPath (Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager-Portable.zip') -Force
$compiler = & (Join-Path $PSScriptRoot 'installer\Get-InnoCompiler.ps1')
& $compiler (Join-Path $PSScriptRoot 'installer\Manager.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows Setup compilation failed.' }
$setup = Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager-Setup.exe'
Write-Output "Installer: $setup"
