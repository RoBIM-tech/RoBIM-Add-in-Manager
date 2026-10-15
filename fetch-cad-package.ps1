$ErrorActionPreference = 'Stop'
# Build-time dependency only; CAD sources and Autodesk SDKs are not part of this repository.
$credentialLines = @('protocol=https','host=github.com','') | git -c credential.helper=manager credential fill
$secretLine = $credentialLines | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
if (!$secretLine) { throw 'Git Credential Manager login is required to fetch the private CAD release.' }
$env:GH_TOKEN = $secretLine.Substring(9)
try {
    $releases = gh api 'repos/gagaggan/RoBIM-CAD/releases?per_page=100' | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'CAD release lookup failed' }
    $selected = $null; $releaseTag = ''
    foreach ($release in $releases) {
        if ($release.draft) { continue }
        $selected = $release.assets | Where-Object name -eq 'DelHiddenNet-AutoCAD2026.zip' | Select-Object -First 1
        if ($selected) { $releaseTag = $release.tag_name; break }
    }
    if (!$selected -or $selected.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') { throw 'Verified CAD release asset not found' }
    $cache = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $target = Join-Path $cache 'DelHiddenNet-AutoCAD2026.zip'
    if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne $selected.digest.Substring(7)) {
        gh release download $releaseTag --repo gagaggan/RoBIM-CAD --pattern 'DelHiddenNet-AutoCAD2026.zip' --dir $cache --clobber
        if ($LASTEXITCODE -ne 0) { throw 'CAD release download failed' }
    }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $selected.digest.Substring(7)) { throw 'CAD release SHA-256 mismatch' }
    $selected.digest.Substring(7) | Set-Content -LiteralPath ($target + '.sha256') -Encoding ASCII
    Write-Output ('Verified CAD dependency: ' + $releaseTag)
} finally { Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue; $credentialLines=$null; $secretLine=$null }
