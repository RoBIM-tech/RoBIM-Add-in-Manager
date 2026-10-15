param([Parameter(Mandatory=$true)][string]$Destination, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
try {
    $Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $current = $Destination
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked installation path is not supported.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    $owned = $false
    $nativeMarker = Join-Path $Destination '.robim-manager-native.ini'
    $legacyMarker = Join-Path $Destination '.robim-manager-install.json'
    foreach ($file in Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked installation file is not supported.' }
    }
    if (Test-Path -LiteralPath $nativeMarker) {
        $lines = @(Get-Content -LiteralPath $nativeMarker)
        $owned = ($lines -contains 'Product=RoBIM.Manager') -and ($lines -contains ('Path=' + $Destination))
    } elseif (Test-Path -LiteralPath $legacyMarker) {
        $record = Get-Content -LiteralPath $legacyMarker -Raw | ConvertFrom-Json
        $owned = $record.Product -eq 'RoBIM.Manager' -and $record.Path -eq $Destination
    }
    if (!$owned -and ($Uninstall -or (Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue | Select-Object -First 1))) { throw 'Unmanaged installation directory. Choose an empty folder.' }
    $exe = Join-Path $Destination 'RoBIM.Manager.exe'
    if (Get-Process -Name 'RoBIM.Manager' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $exe } catch { $true } }) { throw 'Close RoBIM Add-in Manager before continuing.' }
    exit 0
} catch { Write-Error $_; exit 1 }
