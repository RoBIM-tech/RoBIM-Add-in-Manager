#ifndef RepoRoot
  #define RepoRoot SourcePath + ".."
#endif
[Setup]
#ifdef TestBuild
AppId=RoBIM.AddinManager.Test
#else
AppId=RoBIM.AddinManager
#endif
AppName=RoBIM Add-in Manager
AppVersion=1.0.0-preview.38
AppVerName=RoBIM Add-in Manager 1.0.0-preview.38
AppPublisher=RoBIM
AppPublisherURL=https://github.com/gagaggan/RoBIM-Add-in-Manager
DefaultDirName={localappdata}\Programs\RoBIM Manager
DefaultGroupName=RoBIM Add-in Manager
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#RepoRoot}\artifacts
OutputBaseFilename=RoBIM-Manager-Setup
UninstallDisplayName=RoBIM Add-in Manager
UninstallDisplayIcon={app}\RoBIM.Manager.ico
SetupIconFile={#RepoRoot}\assets\RoBIM.Manager.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=1.0.0.38

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#RepoRoot}\artifacts\RoBIM-Manager\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "Guard.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Guard.ps1"; Flags: dontcopy

[Icons]
#ifndef TestBuild
Name: "{group}\RoBIM Add-in Manager"; Filename: "{app}\RoBIM.Manager.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\RoBIM Add-in Manager"; Filename: "{app}\RoBIM.Manager.exe"; WorkingDir: "{app}"
#endif

[INI]
Filename: "{app}\.robim-manager-native.ini"; Section: "Install"; Key: "Product"; String: "RoBIM.Manager"
Filename: "{app}\.robim-manager-native.ini"; Section: "Install"; Key: "Path"; String: "{app}"

[Registry]
#ifndef TestBuild
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\RoBIMManager"; Flags: deletekey; Check: LegacyRegistrationOwned
#endif

[UninstallDelete]
Type: files; Name: "{app}\.robim-manager-native.ini"
Type: files; Name: "{app}\.robim-manager-install.json"

[Code]
function LegacyRegistrationOwned: Boolean;
var Location: String;
begin
  Result := RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\RoBIMManager', 'InstallLocation', Location)
    and (CompareText(Location, ExpandConstant('{app}')) = 0);
end;

function RunGuard(Script: String; Uninstalling: Boolean): Boolean;
var Code: Integer; Args: String;
begin
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" -Destination "' + ExpandConstant('{app}') + '"';
  if Uninstalling then Args := Args + ' -Uninstall';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('Guard.ps1');
  if not RunGuard(ExpandConstant('{tmp}\Guard.ps1'), False) then
    Result := 'RoBIM Manager를 종료하십시오. 기존 설치 기록 또는 설치 경로를 확인할 수 없으면 설치를 진행하지 않습니다.';
end;

function InitializeUninstall: Boolean;
begin
  Result := RunGuard(ExpandConstant('{app}\Guard.ps1'), True);
  if not Result then MsgBox('RoBIM Manager를 종료하고 설치 경로와 설치 기록을 확인하십시오.', mbError, MB_OK);
end;
