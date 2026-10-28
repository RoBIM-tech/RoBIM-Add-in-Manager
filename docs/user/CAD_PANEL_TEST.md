# AutoCAD 패널 직접 확인 — Native-only Manager

이 가이드는 새 정식 설치를 **덮어쓰지 않고** 사용자 Windows PC에서 현재 소스를 테스트하는 방법입니다.

## Manager 패널 미리보기

Windows x64, .NET 10 SDK, Python 3.12+, 인증된 GitHub CLI(`gh auth login`), RoBIM-tech 비공개 저장소 접근이 필요합니다.

```powershell
git clone https://github.com/RoBIM-tech/RoBIM-Add-in-Manager.git
cd RoBIM-Add-in-Manager
pwsh ./scripts/Build-ManagerTestLocal.ps1
```

스크립트는 Libraries 0.1.0 및 CAD 1.0.1 정식 ZIP, Revit Host preview.8을 읽기 전용으로 다운로드하고 SHA-256으로 검사합니다. Manager 현재 소스를 빌드해 `artifacts/RoBIM-Manager/RoBIM.Manager.exe`를 만듭니다. GUI 미리보기는 `artifacts/cad-ribbon-preview.png`입니다.

실제 앱 창에서 **AutoCAD**, **AutoCAD 리본 편집**을 각각 열어 재현합니다. 오류가 나면 Manager 데이터 폴더의 `cad-panel-errors.log`를 확인하세요.

## AutoCAD 내부 RoBIM 리본 확인

위 기본 명령으로 내려받는 CAD 1.0.1 정식 ZIP에는 이번 리본 핫픽스가 포함되지 않습니다. [CAD 검증 절차](https://github.com/RoBIM-tech/RoBIM-CAD/blob/main/docs/CAD_PANEL_HOTFIX_TEST.md)를 먼저 진행하고 ZIP 및 SHA-256을 확보한 다음 아래처럼 지정해야 합니다.

```powershell
pwsh ./scripts/Build-ManagerTestLocal.ps1 -CadHotfixZip "C:\TestPackages\DelHiddenNet-AutoCAD2026.zip"
```

**Loader DLL이 변경되므로 CAD에서 Reload만 눌러서는 수정본이 적용되지 않습니다.** 기존 사용 중인 AutoCAD/Manager 설치 경로를 수동 덮어쓰기하지 마세요. 별도 테스트 환경에서 준비하고 AutoCAD를 종료한 상태에서 관리 설치로 갱신해야 합니다.

## 지원 범위

- pyRevit 저장소는 보관만 하고, 새 Portable Manager 패키지에 pyRevit Reload Bridge가 들어가지 않습니다.
- `-SkipSetup`으로 Native Inno Setup 설치 파일 생성은 생략합니다. 정식 설치/Release는 별도 검증 이후 수행해야 합니다.
- 이 가이드는 원인 진단 및 현재 소스 검증용이며, 실제 사용자 PC AutoCAD 충돌의 복구를 보장하지 않습니다.
