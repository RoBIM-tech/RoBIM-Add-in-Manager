param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($PSScriptRoot)
$marker = Join-Path $destination '.robim-manager-install.json'
if (!(Test-Path -LiteralPath $marker)) { throw 'RoBIM Manager 설치 기록이 없습니다.' }
$record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if ($record.Product -ne 'RoBIM.Manager' -or $record.Path -ne $destination) { throw '설치 경로 검증 실패' }
$expectedExecutable = Join-Path $destination 'RoBIM.Manager.exe'
if (Get-Process -Name 'RoBIM.Manager' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $expectedExecutable } catch { $true } }) { throw 'RoBIM Manager를 먼저 종료하십시오.' }
if (!$Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $answer = [Windows.Forms.MessageBox]::Show("RoBIM Manager를 제거합니까?`n설치된 CAD/Revit 도구·백업·사용자 설정은 유지합니다.`n도구는 제거 전에 관리자에서 개별 제거할 수 있습니다.", 'RoBIM Manager', 'YesNo', 'Question', 'Button2')
    if ($answer -ne 'Yes') { exit 0 }
}
# Validate every deletion before removing the first file. Unknown files are preserved.
$targets = @()
foreach ($name in $record.Files) {
    if ([IO.Path]::GetFileName($name) -ne $name -or $name -match '[:\\/]') { throw '제거 대상 파일 이름 검증 실패' }
    $targets += Join-Path $destination $name
}
$targets += $marker
foreach ($path in $targets) {
    $currentPath = $path
    while ($currentPath) {
        if ((Test-Path -LiteralPath $currentPath) -and ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '연결 경로는 제거하지 않습니다.' }
        $currentPath = [IO.Path]::GetDirectoryName($currentPath)
    }
}
if ($record.Integrated) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RoBIMManager'
    if (Test-Path $key) {
        $registered = Get-ItemProperty $key
        if ($registered.InstallLocation -eq $destination) { Remove-Item -LiteralPath $key }
    }
    $shortcuts = @()
    foreach ($name in @('RoBIM Manager','RoBIM Add-in Manager')) {
        $shortcuts += Join-Path ([Environment]::GetFolderPath('Desktop')) ($name + '.lnk')
        $shortcuts += Join-Path ([Environment]::GetFolderPath('Programs')) ($name + '\' + $name + '.lnk')
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in $shortcuts) {
        if (Test-Path -LiteralPath $path) {
            $shortcut = $shell.CreateShortcut($path)
            if ($shortcut.TargetPath -eq (Join-Path $destination 'RoBIM.Manager.exe')) { Remove-Item -LiteralPath $path }
        }
    }
}
foreach ($path in $targets) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
if (!(Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $destination }
Write-Output 'RoBIM Manager removed. Installed tools and backups were preserved.'
