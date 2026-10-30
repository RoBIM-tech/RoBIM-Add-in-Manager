#ifndef RepoRoot
  #define RepoRoot SourcePath + ".."
#endif
[Setup]
#ifdef TestBuild
#ifdef TestAppId
AppId={#TestAppId}
#else
AppId=RoBIM.AddinManager.Test
#endif
#else
AppId=RoBIM.AddinManager
#endif
AppName=RoBIM Add-in Manager
AppVersion=1.1.1
AppVerName=RoBIM Add-in Manager 1.1.1
AppPublisher=RoBIM
AppPublisherURL=https://github.com/RoBIM-tech/RoBIM-Add-in-Manager
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
VersionInfoVersion=1.1.1.3

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
korean.ConfirmUninstall=RoBIM Manager와 이 Manager가 설치한 Revit·CAD 도구, 연결 모듈, 검색 기록, 캐시 및 복구 백업을 제거하시겠습니까?%n%n개인 설정은 다음 화면에서 삭제/유지를 선택합니다. 다른 제품·pyRevit 본체·GitHub 공용 로그인은 유지합니다.
english.ConfirmUninstall=Remove RoBIM Manager and its managed Revit/CAD tools, host, discovery history, caches and backups?%n%nChoose whether to keep personal settings on the next screen. Other products, pyRevit itself and shared GitHub sign-in are preserved.

[Files]
Source: "{#RepoRoot}\artifacts\RoBIM-Manager\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "Guard.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Guard.ps1"; Flags: dontcopy
#ifndef TestBuild
Source: "{#RepoRoot}\artifacts\RoBIM-Manager\RoBIM.Manager.exe"; DestName: "RuntimeCheck.exe"; Flags: dontcopy
#endif

[Icons]
#ifndef TestBuild
Name: "{group}\RoBIM Add-in Manager"; Filename: "{app}\RoBIM.Manager.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\RoBIM Add-in Manager"; Filename: "{app}\RoBIM.Manager.exe"; WorkingDir: "{app}"
#endif

[Tasks]
#ifndef TestBuild
Name: "startup"; Description: "Windows 로그인 시 RoBIM Manager 자동 시작"; Flags: checkedonce
#endif

[INI]
Filename: "{app}\.robim-manager-native.ini"; Section: "Install"; Key: "Product"; String: "RoBIM.Manager"
Filename: "{app}\.robim-manager-native.ini"; Section: "Install"; Key: "Path"; String: "{app}"

[Registry]
#ifndef TestBuild
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RoBIMManager"; ValueData: """{app}\RoBIM.Manager.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
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
#ifndef TestBuild
var RuntimeCode: Integer; RuntimeReport: AnsiString;
#endif
begin
#ifndef TestBuild
  ExtractTemporaryFile('RuntimeCheck.exe');
  if not Exec(ExpandConstant('{tmp}\RuntimeCheck.exe'), '--verify-setup-runtime "' + ExpandConstant('{tmp}\RoBIM-runtime-check.txt') + '"', '', SW_HIDE, ewWaitUntilTerminated, RuntimeCode) or (RuntimeCode <> 0) then begin
    LoadStringFromFile(ExpandConstant('{tmp}\RoBIM-runtime-check.txt'), RuntimeReport);
    Result := '필요한 .NET 10 x64 실행 환경을 확인하지 못했습니다. Autodesk 설치 복구 또는 Microsoft .NET 실행 환경 설치 후 다시 시도하세요.' + #13#10 + String(RuntimeReport);
    exit;
  end;
#endif
  ExtractTemporaryFile('Guard.ps1');
  if not RunGuard(ExpandConstant('{tmp}\Guard.ps1'), False) then
    Result := 'RoBIM Manager를 종료하십시오. 기존 설치 기록 또는 설치 경로를 확인할 수 없으면 설치를 진행하지 않습니다.';
end;

function InitializeUninstall: Boolean;
begin
  while not RunGuard(ExpandConstant('{app}\Guard.ps1'), True) do begin
    if UninstallSilent then begin
      Result := False;
      exit;
    end;
    if MsgBox('RoBIM Manager를 종료하십시오. 설치 경로 또는 설치 기록에 문제가 있다면 먼저 확인하십시오.' + #13#10 + '종료한 뒤 재시도를 누르면 제거를 계속합니다.', mbError, MB_RETRYCANCEL) <> IDRETRY then begin
      Result := False;
      exit;
    end;
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
#ifndef TestBuild
var Code: Integer; Args: String;
#endif
begin
#ifndef TestBuild
  if CurUninstallStep = usUninstall then begin
    Args := '--uninstall-user-data';
    if UninstallSilent then Args := '--uninstall-user-data-silent';
    if ExpandConstant('{param:KEEPSETTINGS|0}') = '1' then Args := Args + ' --keep-settings';
    if not Exec(ExpandConstant('{app}\RoBIM.Manager.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then Abort;
    if Code <> 0 then Abort;
  end;
#endif
end;
