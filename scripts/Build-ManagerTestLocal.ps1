param(
    [string]$CadHotfixZip,
    [string]$LibraryFeed,
    [switch]$SkipPreview
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows x64 is required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($tool in @('dotnet', 'python', 'gh')) {
    if (!(Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool: install and configure it first." }
}
if (!$LibraryFeed) {
    $LibraryFeed = Join-Path $root 'artifacts/verified-stable-nuget'
    New-Item -ItemType Directory -Path $LibraryFeed -Force | Out-Null
    & gh release download v0.1.0 --repo RoBIM-tech/RoBIM-Libraries --pattern '*.nupkg*' --dir $LibraryFeed --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Private Libraries Release download failed. Run gh auth login first.' }
    foreach ($pkg in Get-ChildItem $LibraryFeed -Filter '*.nupkg' -File) {
        $hashFile = $pkg.FullName + '.sha256'
        if (!(Test-Path $hashFile)) { throw "Missing library checksum: $hashFile" }
        if ((Get-FileHash $pkg.FullName -Algorithm SHA256).Hash -ne (Get-Content $hashFile -Raw).Trim().Split(' ')[0].ToUpperInvariant()) { throw "Library hash mismatch: $($pkg.Name)" }
    }
}
$LibraryFeed = [IO.Path]::GetFullPath($LibraryFeed)
$assets = Join-Path $root 'artifacts/manual-preview-assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$hostName = 'RoBIM-Host-Revit2026.zip'
& gh release download host-v1.0.0-preview.8 --repo RoBIM-tech/RoBIM-Revit-Addin --pattern "$hostName*" --dir $assets --clobber
if ($LASTEXITCODE -ne 0) { throw 'Revit Host preview.8 assets unavailable. Verify GitHub access.' }
$hostZip = Join-Path $assets $hostName
if ($CadHotfixZip) {
    $cadZip = [IO.Path]::GetFullPath($CadHotfixZip)
    if (!(Test-Path ($cadZip + '.sha256'))) { throw 'CAD hotfix ZIP requires its .sha256 sidecar.' }
} else {
    & gh release download v1.0.1 --repo RoBIM-tech/RoBIM-CAD --pattern 'DelHiddenNet-AutoCAD2026.zip*' --dir $assets --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Stable CAD package unavailable. Verify GitHub access.' }
    $cadZip = Join-Path $assets 'DelHiddenNet-AutoCAD2026.zip'
    Write-Warning 'No -CadHotfixZip was passed: only the Manager CAD panel is updated. AutoCAD Loader is still v1.0.1.'
}
foreach ($zip in @($cadZip, $hostZip)) {
    if (!(Test-Path $zip) -or !(Test-Path ($zip + '.sha256'))) { throw "Missing ZIP/sidecar: $zip" }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    $expected = (Get-Content ($zip + '.sha256') -Raw).Trim().Split(' ')[0].ToUpperInvariant()
    if ($actual -ne $expected) { throw "Package SHA-256 mismatch: $zip" }
}
& dotnet run --project (Join-Path $root 'tests/NativeOnlyPolicy/NativeOnlyPolicy.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native-only policy regression failed.' }
& (Join-Path $root 'build-manager.ps1') -LibraryFeed $LibraryFeed -CadPackagePath $cadZip -RevitHostPackagePath $hostZip -SkipSetup
if (!$?) { throw 'Manager Portable build failed.' }
$exe = Join-Path $root 'artifacts/RoBIM-Manager/RoBIM.Manager.exe'
if (!(Test-Path $exe)) { throw 'Portable Manager executable missing.' }
if (!$SkipPreview) {
    $preview = Join-Path $root 'artifacts/cad-ribbon-preview.png'
    & $exe --render-preview --cad-ribbon $preview
    if ($LASTEXITCODE -ne 0 -or !(Test-Path $preview)) { throw 'CAD Ribbon preview failed. Check cad-panel-errors.log.' }
    Write-Host "CAD panel screenshot: $preview"
}
Write-Host "Ready for a manual test (run this EXE as a regular user): $exe"
Write-Host "NO installed Manager/CAD/Revit files have been changed. This is a locally built candidate, not a signed Release."
