param([switch]$Quiet, [switch]$KeepUserData, [switch]$KeepSettings)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($PSScriptRoot)
if (Test-Path -LiteralPath (Join-Path $destination '.robim-manager-native.ini')) { throw 'Setup.exe로 설치된 매니저는 Windows 설정의 설치된 앱에서 제거하십시오.' }
$marker = Join-Path $destination '.robim-manager-install.json'
if (!(Test-Path -LiteralPath $marker)) { throw 'RoBIM Manager 설치 기록이 없습니다.' }
$record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if ($record.Product -ne 'RoBIM.Manager' -or $record.Path -ne $destination) { throw '설치 경로 검증 실패' }
$expectedExecutable = Join-Path $destination 'RoBIM.Manager.exe'
while (Get-Process -Name 'RoBIM.Manager' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $expectedExecutable } catch { $true } }) {
    if ($Quiet) { throw 'RoBIM Manager를 먼저 종료하십시오.' }
    Add-Type -AssemblyName System.Windows.Forms
    if ([Windows.Forms.MessageBox]::Show('RoBIM Manager를 종료한 뒤 재시도를 누르면 제거를 계속합니다.', 'RoBIM 제거', 'RetryCancel', 'Warning') -ne 'Retry') { exit 1 }
}
if (!$Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $message = if ($KeepUserData) { 'Manager 실행 파일만 제거합니다. 도구·설정·백업은 유지합니다.' } else { 'Manager와 관리 중인 Revit·CAD 도구, 연결 모듈, 검색 기록, 캐시 및 백업을 제거합니다. 개인 설정은 다음 화면에서 삭제/유지를 선택합니다. 다른 제품과 GitHub 공용 로그인은 유지합니다.' }
    $answer = [Windows.Forms.MessageBox]::Show($message + "`n계속하시겠습니까?", 'RoBIM Manager', 'YesNo', 'Question', 'Button2')
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
if (!$KeepUserData) {
    $cleanupArgument = if ($Quiet) { '--uninstall-user-data-silent' } else { '--uninstall-user-data' }
    if ($KeepSettings) { $cleanupArgument += ' --keep-settings' }
    $cleanup = Start-Process -FilePath $expectedExecutable -ArgumentList $cleanupArgument -WindowStyle Hidden -Wait -PassThru
    if ($cleanup.ExitCode -ne 0) { throw 'RoBIM 전체 제거가 중단되었습니다. 도구·설정 정리를 완료하지 못해 Manager를 유지합니다.' }
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
Write-Output 'RoBIM Manager removal completed.'
