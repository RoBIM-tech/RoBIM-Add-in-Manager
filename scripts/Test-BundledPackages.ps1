param([Parameter(Mandatory)][string]$RevitHostPackagePath,
      [Parameter(Mandatory)][string]$CadPackagePath,
      [switch]$AllowCandidateCad)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packages = Get-Content -LiteralPath (Join-Path $root 'release-packages.lock.json') -Raw | ConvertFrom-Json
function Digest([string]$zip) {
    if (!(Test-Path -LiteralPath $zip) -or !(Test-Path -LiteralPath ($zip + '.sha256'))) { throw "Missing package/sidecar: $zip" }
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($actual -ne (Get-Content -LiteralPath ($zip + '.sha256') -Raw).Trim()) { throw "Package checksum mismatch: $zip" }
    return $actual
}
$hostDigest = Digest $RevitHostPackagePath
if ($hostDigest -ne $packages.revitHost.sha256) {
    throw "Fixed Revit Host must match official $($packages.revitHost.tag); a matching local sidecar alone is insufficient. Download the locked Release asset."
}
$cadDigest = Digest $CadPackagePath
if (!$AllowCandidateCad -and $cadDigest -ne $packages.cad.sha256) {
    throw "Release CAD package must match official $($packages.cad.tag). Candidate CAD packages are allowed only in Portable test builds."
}
Write-Output "Verified official Revit Host $($packages.revitHost.version); CAD $($packages.cad.version) or explicitly allowed Portable candidate."
