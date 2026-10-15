param([switch]$SkipCadBuild)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# Compatibility switch retained for existing build commands. CAD is a separately released package.
$cadPackage = Join-Path $PSScriptRoot 'artifacts\DelHiddenNet-AutoCAD2026.zip'
if (!(Test-Path -LiteralPath $cadPackage) -or !(Test-Path -LiteralPath ($cadPackage + '.sha256'))) {
    & (Join-Path $PSScriptRoot 'fetch-cad-package.ps1')
}
if ((Get-FileHash -LiteralPath $cadPackage).Hash -ne (Get-Content -LiteralPath ($cadPackage + '.sha256') -Raw).Trim()) { throw 'Cached CAD release SHA-256 mismatch' }
$output = Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager'
if (Test-Path -LiteralPath $output) {
    Get-ChildItem -LiteralPath $output -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}
& dotnet publish (Join-Path $PSScriptRoot 'RoBIM.Manager\RoBIM.Manager.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --source https://api.nuget.org/v3/index.json -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Manager publish failed.' }
$cad = Join-Path $PSScriptRoot 'artifacts\DelHiddenNet-AutoCAD2026.zip'
Copy-Item -LiteralPath $cad -Destination $output -Force
$bridge = Join-Path $output 'RoBIM-ReloadBridge.zip'
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'reload-bridge\RoBIM Manager.extension') -DestinationPath $bridge -Force
(Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash | Set-Content -LiteralPath ($bridge + '.sha256') -Encoding ASCII
(Get-FileHash -LiteralPath $cad -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $output 'DelHiddenNet-AutoCAD2026.zip.sha256') -Encoding ASCII
foreach ($name in @('Install.ps1','Uninstall.ps1')) {
    $source = Join-Path $PSScriptRoot "installer\$name"
    [IO.File]::WriteAllText((Join-Path $output $name), [IO.File]::ReadAllText($source), [Text.UTF8Encoding]::new($true))
}
Compress-Archive -LiteralPath $output -DestinationPath (Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager-Portable.zip') -Force
& dotnet publish (Join-Path $PSScriptRoot 'RoBIM.Setup\RoBIM.Setup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --source https://api.nuget.org/v3/index.json -o (Join-Path $PSScriptRoot 'artifacts\setup-build') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
$setup = Join-Path $PSScriptRoot 'artifacts\RoBIM-Manager-Setup.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'artifacts\setup-build\RoBIM-Manager-Setup.exe') -Destination $setup -Force
Write-Output "Installer: $setup"
