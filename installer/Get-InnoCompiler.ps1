$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $root 'artifacts\tools'
$compiler = Join-Path $toolRoot 'inno\ISCC.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
    $download = Join-Path $toolRoot 'innosetup.exe'
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_4_3/innosetup-6.4.3.exe' -OutFile $download
    if ((Get-FileHash -LiteralPath $download).Hash -ne 'F3C42116542C4CC57263C5BA6C4FEABFC49FE771F2F98A79D2F7628B8762723B') { throw 'Inno compiler SHA-256 mismatch' }
    if ((Get-AuthenticodeSignature -LiteralPath $download).Status -ne 'Valid') { throw 'Inno compiler signature invalid' }
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS',('/DIR="' + (Join-Path $toolRoot 'inno') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw 'Compiler setup failed' }
}
Write-Output $compiler
