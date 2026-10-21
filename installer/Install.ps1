param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\RoBIM Manager'),
    [switch]$Quiet,
    [switch]$SkipIntegration
)
$ErrorActionPreference = 'Stop'
function Assert-NoLinks([string]$Path) {
    $currentPath = [IO.Path]::GetFullPath($Path)
    while ($currentPath) {
        if (Test-Path -LiteralPath $currentPath) {
            if ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '연결 폴더에는 설치하지 않습니다.' }
        }
        $currentPath = [IO.Path]::GetDirectoryName($currentPath)
    }
}
try {
    $Destination = [IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath (Join-Path $Destination '.robim-manager-native.ini')) { throw 'Setup.exe로 설치된 매니저는 Setup.exe로 업데이트하십시오.' }
    Assert-NoLinks $Destination
    $marker = Join-Path $Destination '.robim-manager-install.json'
    Assert-NoLinks $marker
    if ((Test-Path -LiteralPath $Destination) -and (Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1)) {
        if (!(Test-Path -LiteralPath $marker)) { throw '기존 폴더가 RoBIM Manager 설치 폴더가 아니므로 덮어쓰지 않습니다.' }
        $oldRecord = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($oldRecord.Product -ne 'RoBIM.Manager' -or $oldRecord.Path -ne $Destination) { throw '기존 설치 기록 검증 실패' }
    }
    $expectedExecutable = Join-Path $Destination 'RoBIM.Manager.exe'
    $running = Get-Process -Name 'RoBIM.Manager' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $expectedExecutable } catch { $true } }
    if ($running) { throw 'RoBIM Manager를 종료한 뒤 설치하십시오.' }
    $files = Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.exe','.dll','.json','.zip','.sha256','.ps1','.md','.ico','.txt' -and $_.Name -ne 'RoBIM-Manager-Setup.exe' }
    $required = @('RoBIM.Manager.exe','Uninstall.ps1','DelHiddenNet-AutoCAD2026.zip','DelHiddenNet-AutoCAD2026.zip.sha256','RoBIM-ReloadBridge.zip','RoBIM-ReloadBridge.zip.sha256','RoBIM-Host-Revit2026.zip','RoBIM-Host-Revit2026.zip.sha256')
    foreach ($name in $required) { if ($files.Name -notcontains $name) { throw "설치 파일 누락: $name" } }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $backupPath = Join-Path ([IO.Path]::GetDirectoryName($Destination)) ('RoBIM-Manager-backup-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupPath | Out-Null
    $changed = @()
    try {
        foreach ($file in $files) {
            $target = Join-Path $Destination $file.Name
            Assert-NoLinks $target
            if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backupPath $file.Name) }
            $changed += $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        if (!$SkipIntegration) {
            $shell = New-Object -ComObject WScript.Shell
            $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'RoBIM Add-in Manager'
            Assert-NoLinks $startMenu
            New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
            $shortcut = $shell.CreateShortcut((Join-Path $startMenu 'RoBIM Add-in Manager.lnk'))
            $shortcut.TargetPath = Join-Path $Destination 'RoBIM.Manager.exe'
            $shortcut.WorkingDirectory = $Destination
            $shortcut.Save()
            $desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'RoBIM Add-in Manager.lnk'))
            $desktopShortcut.TargetPath = $shortcut.TargetPath
            $desktopShortcut.WorkingDirectory = $Destination
            $desktopShortcut.Save()
            $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RoBIMManager'
            New-Item -Path $key -Force | Out-Null
            $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $values = @{ DisplayName='RoBIM Add-in Manager'; DisplayVersion='1.0.0-preview.25'; Publisher='RoBIM'; InstallLocation=$Destination; DisplayIcon=$shortcut.TargetPath; UninstallString=('"' + $powerShell + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $Destination 'Uninstall.ps1') + '"') }
            foreach ($entry in $values.GetEnumerator()) { New-ItemProperty -Path $key -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null }
        }
        @{ Product='RoBIM.Manager'; Version='1.0.0-preview.25'; Path=$Destination; Files=@($files.Name); Integrated=(!$SkipIntegration) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $marker -Encoding UTF8
        if (!$SkipIntegration) {
            foreach ($legacy in @((Join-Path ([Environment]::GetFolderPath('Desktop')) 'RoBIM Manager.lnk'), (Join-Path ([Environment]::GetFolderPath('Programs')) 'RoBIM Manager\RoBIM Manager.lnk'))) {
                Assert-NoLinks $legacy
                if (Test-Path -LiteralPath $legacy) {
                    if ($shell.CreateShortcut($legacy).TargetPath -eq $expectedExecutable) { Remove-Item -LiteralPath $legacy }
                }
            }
        }
    }
    catch {
        foreach ($name in $changed) {
            $saved = Join-Path $backupPath $name
            $target = Join-Path $Destination $name
            if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        }
        throw
    }
    # Only files created by this installer are removed from the temporary backup.
    foreach ($file in Get-ChildItem -LiteralPath $backupPath -File) { Remove-Item -LiteralPath $file.FullName -Force }
    Remove-Item -LiteralPath $backupPath
    if (!$Quiet) {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show("RoBIM Manager 설치를 완료했습니다.`n시작 메뉴 또는 바탕화면에서 실행하십시오.`n`nCAD/Revit 도구는 관리자에서 선택해 설치합니다.", 'RoBIM Manager') | Out-Null
    }
    Write-Output "Installed: $Destination"
}
catch {
    if (!$Quiet) { Add-Type -AssemblyName System.Windows.Forms; [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'RoBIM Manager 설치 실패') | Out-Null }
    throw
}
