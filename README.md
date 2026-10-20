# RoBIM Add-in Manager

Revit·AutoCAD의 실제 기능 버튼을 카드로 확인하고 설치·제거·업데이트·Reload를 관리하는 Windows 앱입니다. 독립 Revit Add-in과 pyRevit 목록을 별도로 제공하며 설치된 버튼의 리본 배치를 편집합니다.

## 사용자 흐름

1. [Releases](https://github.com/gagaggan/RoBIM-Add-in-Manager/releases)의 Setup 또는 검증한 작업본의 `artifacts/RoBIM-Manager-Setup.exe`로 설치합니다. Windows 시작 메뉴의 **RoBIM Add-in Manager**를 실행합니다.
2. **Revit 독립 Add-in / Revit · pyRevit / AutoCAD**에서 사용할 버튼을 선택합니다. 전체 목록은 설치 가능한 도구이며 실제 리본에는 설치·적용한 명령만 표시합니다.
3. 호스트가 실행 중이면 지원 범위의 변경을 준비하고 해당 Reload로 반영합니다. 최초 연결·고정 로더 교체 등 종료가 필요한 경우 화면 안내를 따릅니다.
4. **리본 편집**에서 탭·패널·표시 이름·위치를 편집합니다. 큰 버튼은 한 칸, 작은 버튼은 한 칸의 2/3단 묶음입니다. 구분선과 드래그 앞/뒤 삽입·탭 호버 이동을 지원하며 저장과 실제 적용을 구분합니다.
5. **설치 위치**에서 pyRevit 확장의 상위 폴더를 선택할 수 있습니다. Revit 종료 후 관리 설치를 백업·검증해 이전하고 검색 경로에 등록합니다.
6. 제거·복원은 소유권과 파일 해시를 확인하며 사용자 변경과 공용 의존성을 보존합니다. 외부 설치는 검증 없이 덮어쓰거나 지우지 않습니다.

Revit 안의 연결은 **RoBIM 관리 → 독립 Add-in → Reload** 및 **RoBIM 관리 → Manager → pyRevit Reload**, CAD는 **RoBIM → 관리 → Reload**입니다. 준비됨은 실제 적용 완료와 다릅니다.

현재 소스의 검증 이력은 [릴리스 노트](docs/MANAGER_RELEASE_NOTES.md), 사용자 조작은 [사용설명서](docs/USER_GUIDE_KO.md)를 확인합니다. 로컬 작업본 검증 버전과 GitHub Release 배포 버전은 다를 수 있습니다.

## 저장소 소유권

| 저장소 | 담당 |
| --- | --- |
| 이 저장소 | Manager.Core, WinForms UI, Setup, Manager 소유 pyRevit 연결 |
| [RoBIM-pyrevit](https://github.com/gagaggan/RoBIM-pyrevit) | Python 버튼·lib·기본 배치 |
| [RoBIM-Revit-Addin](https://github.com/gagaggan/RoBIM-Revit-Addin) | Native Host·독립 기능·Reload 계약 |
| [RoBIM-CAD](https://github.com/gagaggan/RoBIM-CAD) | CAD Loader·기능·버튼 계약 |
| [RoBIM-Workspace](https://github.com/gagaggan/RoBIM-Workspace) | 통합 규칙·호환성·스킬 동기화 |

NativeProtocol.cs와 CadConfiguration.cs는 SDK 없는 컴파일을 위한 계약 사본입니다. 기능 소스 복사본과 구분하며 통합 저장소에서 공급 원본과 비교합니다. `.codex/skills/robim-manager`가 전용 스킬 원본입니다.

## 개발·배포

Windows x64 / .NET SDK 10 / Git. CAD·Host의 완성된 Release ZIP과 SHA-256을 입력으로 사용하며 다른 저장소를 빌드하거나 원격 설치 스크립트를 실행하지 않습니다.

```powershell
./build-manager.ps1 -CadPackagePath '<CAD ZIP>' -RevitHostPackagePath '<Host ZIP>'
./tests/run-manager-tests.ps1
./tests/test-manager-installer.ps1
./tests/test-native-installer.ps1
```

출력은 `artifacts/RoBIM-Manager-Setup.exe`와 Portable ZIP입니다. 설치 엔진·경로·로컬 수정 보호, UI·성능·계약 검사를 실제 호스트 명령 실행과 구분합니다. Setup은 Inno Setup으로 생성하며 런타임·의존성 라이선스를 동봉합니다.

실제 데이터 경로 검증은 `WindowsDataPath`를 사용합니다. Codex 앱 전용 LocalCache에만 파일이 존재하는 상태를 일반 사용자 설치 성공으로 취급하지 않습니다. 일반 Windows 실행의 경로·Revit 저널·적용 세션으로 확인한 기록은 [Preview 24 검증](docs/PREVIEW24_VALIDATION.md)에 있습니다.

[설치 패키지 규격](docs/EXTENSION_PACKAGES.md) · [기술 구조](docs/MANAGER.md) · [버튼 구성](docs/BUTTON_CATALOG_ARCHITECTURE.md)

Reload 연결의 .NET 8/IronPython 참조 검사는 설치된 pyRevit DLL을 읽어 사용합니다. 테스트 DLL은 배포 패키지에 동봉하지 않습니다.

```powershell
dotnet run --project tests/ReloadBridgeImports -- '<Reload script.py>' '<Manager EXE>'
```

기본 경로는 Program Files의 pyRevit-Master와 Revit 2026입니다. 다른 위치는 MSBuild의 `PyRevitRoot`, `CodeDomAssembly` 속성으로 지정합니다. 검사 결과와 실제 Revit Reload 확인은 [Reload 수정 검증](docs/RELOAD_IMPORT_FIX.md)을 참조합니다.
