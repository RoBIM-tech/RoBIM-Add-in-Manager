$ErrorActionPreference = 'Stop'
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-packages.lock.json') -Raw | ConvertFrom-Json
$package = $lock.cad
$release = gh api ("repos/" + $package.repository + "/releases/tags/" + $package.tag) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Locked CAD release lookup failed' }
$selected = $release.assets | Where-Object name -eq $package.asset | Select-Object -First 1
if (!$selected -or $selected.digest -notmatch '^sha256:[a-fA-F0-9]{64}$' -or $selected.digest.Substring(7) -ne $package.sha256) { throw 'Locked CAD release asset verification failed' }
$cache = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$target = Join-Path $cache 'RoBIM-CAD-Loader-AutoCAD2026.zip'
if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne $package.sha256) {
    $download = Join-Path $cache ('cad-download-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $download | Out-Null
    gh release download $package.tag --repo $package.repository --pattern $package.asset --dir $download
    if ($LASTEXITCODE -ne 0) { throw 'CAD release download failed' }
    $source = Join-Path $download $package.asset
    if ((Get-FileHash -LiteralPath $source).Hash -ne $package.sha256) { throw 'CAD release SHA-256 mismatch' }
    Copy-Item -LiteralPath $source -Destination $target -Force
}
$package.sha256 | Set-Content -LiteralPath ($target + '.sha256') -Encoding ASCII
Write-Output ('Verified CAD dependency: ' + $package.tag)
