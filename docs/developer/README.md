# Manager 개발자 설명서

Manager의 카드·선택 설치·Reload·사용자 리본이 어떤 순서로 동작하는지 설명합니다. 사용자 조작은 [일반 사용자 설명서](../user/README.md), 패키지 형식의 상세는 [기존 패키지 규격](../EXTENSION_PACKAGES.md)을 참고합니다.

## 프로젝트와 진입점

| 경로 | 역할 |
| --- | --- |
| `RoBIM.Manager/Program.cs` | 실행 인자·진단 명령·앱 시작, 실제 Windows 데이터 경로 검사 |
| `RoBIM.Manager` | WinForms 카드·검색·설치 위치·버튼 선택·리본 편집 |
| `Manager.Core` | 카탈로그·GitHub/패키지·설치 기록·백업·프로필·준비/적용 정책 |
| `reload-bridge/RoBIM Manager.extension` | Manager 소유 pyRevit Reload·리본 연결 |
| `installer`, `build-manager.ps1` | 검증된 CAD/Native Host ZIP 동봉, Portable·Setup 생성 |
| `Manager.Tests`, `tests` | 임시 경로 설치·계약·선택·실패 복원·패키지 검사 |

Manager는 .NET 10 Windows x64 앱이며 Autodesk SDK를 참조하지 않습니다. Native/CAD 기능 알고리즘은 각 제품 저장소가 소유합니다.

## 카탈로그에서 카드까지

- **pyRevit:** `Manager.Core/RevitButtons.cs`가 layout을 따라 bundle의 title·tooltip·아이콘과 제한된 script 문자열 메타데이터를 읽습니다. 검색 과정에서 명령 코드를 실행하지 않습니다. 운영 경로와 TEST/미공개 경로를 구분합니다. 버튼 ID는 원본의 상대 bundle 경로입니다.
- **독립 Revit:** `robim-module.json`의 모듈 ID·버전·commands를 읽어 명령별 카드를 만듭니다. 명령 선택은 모듈 ID/command ID에 연결합니다.
- **CAD:** `robim-cad.json`의 modules/actions를 읽습니다. 명령 키는 `모듈ID:액션ID`입니다. 한 모듈에 여러 버튼이 있을 수 있습니다.

검색 결과·다운로드 캐시·설치된 명령은 서로 다른 상태입니다. 원격 목록 조회 실패를 미설치로 바꾸거나 이미 설치한 명령의 제거를 원격 조회 성공에 종속시키지 않습니다.

## 설치와 복구의 공통 흐름

`InstallService`는 파일 소유권·관리 경로·해시를 확인하고 백업 후 설치 기록을 갱신합니다. 외부 설치나 변경된 관리 파일은 검증 없이 덮어쓰지 않습니다. 작업 잠금과 쓰기 직전의 호스트 상태 확인을 유지합니다. 실패하면 원래 파일·기록·활성화 상태를 복원할 수 있어야 합니다.

```text
카드 선택 → 설치/변경 상태 검사 → 검증된 입력 준비
          → 호스트 종료 설치 또는 지원되는 실행 중 준비
          → 해당 호스트가 적용 → 결과 확인 → 카드 상태 갱신
```

Manager 앱의 업데이트, 공용 패키지 업데이트, 명령 선택 변경은 각각 다른 작업입니다. 폴더가 존재한다는 사실만으로 Manager 관리 설치이거나 최신 선택이 적용됐다고 표시하지 않습니다.

## pyRevit 선택 설치와 Reload

1. 검증한 원본 트리와 운영 버튼 목록을 읽습니다.
2. `RevitButtons.Prepare` 등이 선택 프로필을 만들고 공용 lib/dll 및 다른 명령이 직접 호출하는 의존성을 보존합니다.
3. 선택 해제된 bundle의 실제 파일 처리와 복구 캐시를 관리합니다. root/tab의 빈 layout이 전체 표시로 해석되는 문제도 고려합니다. 모든 경우를 단순 숨김으로 설명하지 않습니다.
4. 호스트 실행 중이면 기존 바이너리·설치 기록·대상 세션과 호환되는 Python 변경을 준비합니다.
5. Manager 연결 확장이 `--apply-revit-reload PID StartTicks`로 적용을 요청한 뒤 표준 pyRevit Reload를 호출합니다. pyRevit core를 패치하지 않습니다.
6. 실패는 기록과 기존 프로필을 보존하며 재시도 가능한 상태로 남깁니다.

Python 버튼을 독립 C# 모듈로 자동 변환하지 않습니다. `script.cs` 등 실행 바이너리와 개발 전용 tests 프로젝트를 구별해 불필요한 재시작 차단을 피하되 실제 바이너리 변경 검사는 생략하지 않습니다.

## Native 모듈 Reload

`NativeRevitCommands`는 가져온 기능 패키지의 모듈·버전·해시와 원하는 선택을 관리합니다. 여러 모듈을 업데이트할 때 모든 입력을 확인한 뒤 요청을 게시합니다. 고정 Host ZIP은 별도입니다.

Host가 Revit API 컨텍스트에서 요청을 다시 검증하고 고유 실행 복사본·격리 로드 컨텍스트·dispatch를 교체합니다. Manager는 요청 ID와 PID/시작 시각이 맞는 응답으로 실제 적용을 확인합니다. 상세 실행 주체는 [Native 개발 문서](https://github.com/gagaggan/RoBIM-Revit-Addin/blob/codex/manager-workspace-cleanup/docs/developer/README.md)입니다.

일반 .addin 직접 등록 패키지와 `robim-module.json`의 command-only 모듈은 경로가 다릅니다. 파일이 DLL이라는 이유만으로 모든 패키지가 실행 중 Reload 가능하다고 설명하지 않습니다. 이미 로드된 Host 변경과 초기 전환은 해당 종료 정책을 따릅니다.

## CAD 스냅샷 Reload

Manager는 고정 Loader·설치 해시·선택 액션과 상대 경로를 검사하고 CAD 데이터 영역에 스냅샷과 요청을 준비합니다. `ROBIMRELOAD`가 요청·세션·Loader 호환성·파일 해시를 확인해 활성 구성과 응답을 기록합니다. 준비 파일 생성만으로 적용 완료를 표시하지 않습니다.

현재 공급 방식은 CAD bundle ZIP입니다. 카탈로그에 모듈별 버전이 있어도 임의 저장소의 feature ZIP들을 자동 조합하는 일반 해결기가 구현됐다는 뜻은 아닙니다. 상세는 [CAD 개발 문서](https://github.com/gagaggan/RoBIM-CAD/blob/codex/manager-workspace-cleanup/docs/developer/README.md)와 [저장소 분리 검토](https://github.com/gagaggan/RoBIM-Workspace/blob/codex/user-developer-guides/docs/developer/BUTTON_REPOSITORY_REVIEW.md)를 확인합니다.

## 리본 배치

기능 ID와 사용자 표시 이름·탭·패널·순서·구분선·2/3단 묶음을 분리합니다. 한 칸에 2개/3개 명령을 쌓는 구조이며 작은 이미지만 지정하는 옵션과 다릅니다. 드래그의 앞/뒤 삽입, 묶음 이동, 탭 호버 전환, 편집 입력 위치를 모델과 WinForms 이벤트 양쪽에서 확인합니다.

저장된 사용자 배치를 공급 기본값으로 초기화하지 않습니다. **저장**, **준비**, **호스트 적용**을 분리합니다. Native/pyRevit 리본의 소유권과 Revit의 제한 때문에 선택 해제 후 숨김·dispatch 해제·파일 정리·다음 재시작의 구성은 같은 단계가 아닙니다.

## 상태와 경로

기본 관리 데이터는 `%LOCALAPPDATA%/RoBIM/Manager`입니다. `installed.json`은 관리 설치와 해시, 백업은 복구 자료, Native/CAD 하위 데이터는 카탈로그·요청·응답·실행 복사본입니다. pyRevit 설치 위치는 사용자 선택에 따라 다를 수 있으며 연결 확장은 별도 기본 위치를 사용합니다.

`WindowsDataPath`가 논리 경로와 파일 핸들의 실제 경로를 검사합니다. 앱 격리 LocalCache와 일반 AppData를 같은 설치로 취급하지 않습니다. 인증정보는 Windows 자격 증명에 두고 로그·INI·Git에 복사하지 않습니다.

## 빌드와 확인

```powershell
./build-manager.ps1 -CadPackagePath '<검증한 CAD ZIP>' -RevitHostPackagePath '<검증한 Host ZIP>'
./tests/run-manager-tests.ps1
./tests/test-manager-installer.ps1
./tests/test-native-installer.ps1
```

프로그램의 `--verify-card-ui '<결과 JSON>'`는 카드·리본 편집 UI 검사에 사용합니다. `tests/ReloadBridgeImports`는 설치된 pyRevit IronPython으로 CLR import·프로세스·Manager EXE 버전 조회를 검사합니다. 실행 인자는 해당 프로젝트 소스와 [Reload 수정 기록](../RELOAD_IMPORT_FIX.md)을 참고합니다.

NativeProtocol/CadConfiguration 사본은 통합 저장소에서 공급 원본과 비교합니다. Release ZIP에 Autodesk SDK·사용자 모델·개발 캐시를 넣지 않습니다. 로컬 Setup 검증과 GitHub Release 게시는 구분합니다.

[문서 목록](../README.md) · [일반 사용자 설명서](../user/README.md)

## 공용 패키지와 신규 도구 등록

현재 Common 의존성은 `RoBIM.Common [0.1.0-test.2]`입니다. 초기 TEST 패키지는 Libraries clone에서 로컬 공급원으로 만듭니다.

```powershell
./restore-libraries.ps1 -LibrariesPath <RoBIM-Libraries clone>
python scripts/validate_registration.py
./tests/run-manager-tests.ps1
./build-manager.ps1
```

Manager는 설치 중 외부 저장소의 빌드 스크립트를 실행하지 않습니다. 위 복원은 개발자가 명시적으로 실행하는 빌드 준비입니다. 중앙 `catalog/modules.json`은 앱에 리소스로 포함되어 시작·화면 전환 시 네트워크 조회를 추가하지 않습니다. 사용자 URL 등록은 별도 목록으로 보존합니다.

신규 명령은 중앙 목록과 해당 저장소의 robim-package/기능 매니페스트에 함께 추가합니다. `python scripts/validate_registration.py --source <기능 clone> --host revit --repository owner/repo`로 확인합니다. 배포 전 등록 PR이 반영되어야 합니다. Git 저장소 생성만으로 모든 사용자 Manager에 즉시 나타나는 것은 아닙니다.

Revit `robim-release.json`, CAD robim-cad.json module의 channel·validation·libraries를 Common 정책으로 읽습니다. TEST는 별도 메뉴로 분리하며 일반 목록의 설치 성공을 실사용 승격으로 표현하지 않습니다. TEST 파일을 가져오는 것만으로 기존 실사용 선택 채널을 바꾸지 않습니다. GitHub 일반 다운로드는 prerelease를 건너뛰며 명시적 test 패키지에서만 TEST 릴리스를 선택합니다.

## pyRevit Reload와 독립 관리 버튼의 공존

pyRevit의 UI cleanup은 같은 pyRevit 탭에 있던 미갱신 Native 버튼/패널에도 Enabled=false를 설정할 수 있습니다. Manager 연결에서 Visible만 복원하면 버튼이 보이면서 회색으로 남습니다. Preview 26은 실제 설치/활성 선택에서 해석한 버튼에 한해 API Enabled=true를 복원하고, 소유 탭/목적지 패널도 활성화합니다. Revit 명령의 availability나 모델 편집 상태를 우회하지 않으며 다른 제품·미설치 버튼은 복원하지 않습니다. start마다 fingerprint/warmup을 초기화해 pyRevit UI cleanup이 끝난 뒤 Idling에서 다시 적용합니다.

revit-ui-state.json의 EnabledBeforeRestore, Enabled, UiEnabled, PanelEnabled는 복원 전후 상태를 기록합니다. tests/test-ribbon-activation.py는 실제 apply_layout 코드를 가짜 리본에 적용하여 두 관리 버튼과 부모 복원, 다른 제품/제거 버튼 보존, 미설치 탭 미생성, 반복 적용을 검사합니다. run-manager-tests.ps1에서 함께 실행합니다. pyRevit core와 Native Host DLL은 이 수정에 포함되지 않습니다.


## 저장소 이동과 단일 버튼 등록

신규 Revit 저장소는 중앙 목록 한 패키지·한 명령과 기능 descriptor가 일치해야 합니다. validate_registration.py는 두 번째 명령, 서로 다른 패키지 정의, 다른 저장소 ProjectReference, 부정확한 RoBIM 의존성 버전을 거부합니다.

catalog/modules.json의 repositoryMoves는 구 Native 저장소에 등록된 중앙 소유 패키지만 새 기본 저장소로 연결합니다. Registered()는 사용자 파일을 쓰지 않고 모르는 패키지·새 저장소의 사용자 브랜치 선택을 유지합니다. 시트의 일대일 이동은 Tool 설치 ID도 구 identity로 계산해 기존 레코드를 유지합니다. 이 이전 정의를 임의 제거하면 설치 ID가 달라지므로 호환 계약으로 보존합니다. 모듈/명령 ID는 저장소 이동으로 바꾸지 않습니다.

신규 소스 생성은 Workspace 도구를 사용하고 Manager와 Workspace PR을 함께 검토합니다. 중앙 목록 변경은 앱 소스 반영이며 사용자 PC는 새 목록이 포함된 Manager 배포 또는 URL 등록이 필요합니다. 새 저장소 자체는 자동 설치가 아닙니다.
