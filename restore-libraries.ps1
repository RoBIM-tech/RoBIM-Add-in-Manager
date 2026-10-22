param([Parameter(Mandatory=$true)][string]$LibrariesPath)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$libraryRoot = [IO.Path]::GetFullPath($LibrariesPath)
$project = Join-Path $libraryRoot 'src/RoBIM.Common/RoBIM.Common.csproj'
if (!(Test-Path -LiteralPath $project)) { throw 'Clone RoBIM-Libraries and pass its root.' }
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($version -ne '0.1.0-test.2') { throw 'Expected RoBIM.Common 0.1.0-test.2 source.' }
dotnet pack $project -c Release -o (Join-Path $libraryRoot 'artifacts/packages')
if ($LASTEXITCODE -ne 0) { throw 'Common package build failed' }
dotnet restore (Join-Path $PSScriptRoot 'RoBIM.Manager/RoBIM.Manager.csproj') --source 'https://api.nuget.org/v3/index.json' --source (Join-Path $libraryRoot 'artifacts/packages')
if ($LASTEXITCODE -ne 0) { throw 'Manager package restore failed' }
