# Manager 개발자 설명서

[Manager 전체 제거와 개인 설정 선택](UNINSTALL.md): preview.44의 설치 제거 경로·안전 검사·검증 범위.

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
- **독립 Revit:** 중앙 목록의 모듈 ID/command ID로 다운로드 전에도 바로 설치할 단일 Native 카드를 만듭니다. 설치 시 `SingleNativeButton.Resolve`가 검증된 Release ZIP을 받거나 ID로 찾은 캐시를 사용하고, 명령이 하나이며 등록된 ID와 일치하는지 검사합니다. `robim-module.json`을 읽는 별도 기능 목록 단계는 노출하지 않습니다.
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

## 단일 버튼 설치와 표시 이름

`ToolNames.Display`는 화면 이름의 RoBIM 접두어만 제거합니다. 원격 package 레코드와 저장소·package/module/command ID는 바꾸지 않으며 다운로드의 메타데이터 일치 검사도 유지합니다.

Release 다운로드를 준비할 때 `runtime/<package.Folder>`를 사용해 bundle 폴더명을 유지합니다. 이전 Manager가 만든 `runtime` 이름의 캐시도 `SingleNativeButton.Registration`의 모듈 ID로 찾아 재사용합니다. 등록 저장소가 소유한 모듈을 로컬 가져오기 카드로 다시 추가하지 않습니다. 시작·탭 전환에서 원격 다운로드하지 않고 설치 또는 명시적 업데이트 확인 시에만 요청합니다.

설치 준비가 완료되면 `RibbonLayouts.EnsureInstalled`가 기본 탭/패널에 해당 명령 하나를 추가합니다. 기존 사용자 위치·이름·2/3단 묶음·구분선과 다른 버튼은 유지하고, 같은 ID가 있으면 파일을 다시 쓰지 않습니다. 리본 파일이 없는 경우 기존 선택 버튼의 기본 배치도 함께 보존합니다. 실제 리본은 Native Host Reload 후 생성되고 리본 연결이 저장 배치를 적용합니다.

`Manager.Tests --check-native-downloads <임시경로>`는 실제 인증/Release ZIP/해시/단일 명령/선택 준비/기본 배치를 임시 영역에서 확인합니다. 사용자 모델이나 실제 선택을 바꾸지 않습니다.

## Native 모듈 Reload

`NativeRevitCommands`는 가져온 기능 패키지의 모듈·버전·해시와 원하는 선택을 관리합니다. 여러 모듈을 업데이트할 때 모든 입력을 확인한 뒤 요청을 게시합니다. 고정 Host ZIP은 별도입니다.

Host가 Revit API 컨텍스트에서 요청을 다시 검증하고 고유 실행 복사본·격리 로드 컨텍스트·dispatch를 교체합니다. Manager는 요청 ID와 PID/시작 시각이 맞는 응답으로 실제 적용을 확인합니다. 상세 실행 주체는 [Native 개발 문서](https://github.com/RoBIM-tech/RoBIM-Revit-Addin/blob/main/docs/developer/README.md)입니다.

일반 .addin 직접 등록 패키지와 `robim-module.json`의 command-only 모듈은 경로가 다릅니다. 파일이 DLL이라는 이유만으로 모든 패키지가 실행 중 Reload 가능하다고 설명하지 않습니다. 이미 로드된 Host 변경과 초기 전환은 해당 종료 정책을 따릅니다.

## CAD 스냅샷 Reload

Manager는 고정 Loader·설치 해시·선택 액션과 상대 경로를 검사하고 CAD 데이터 영역에 스냅샷과 요청을 준비합니다. `ROBIMRELOAD`가 요청·세션·Loader 호환성·파일 해시를 확인해 활성 구성과 응답을 기록합니다. 준비 파일 생성만으로 적용 완료를 표시하지 않습니다.

현재 공급 방식은 CAD bundle ZIP입니다. 카탈로그에 모듈별 버전이 있어도 임의 저장소의 feature ZIP들을 자동 조합하는 일반 해결기가 구현됐다는 뜻은 아닙니다. 상세는 [CAD 개발 문서](https://github.com/RoBIM-tech/RoBIM-CAD/blob/main/docs/developer/README.md)와 [저장소 분리 검토](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_REPOSITORY_REVIEW.md)를 확인합니다.

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

Preview 32의 Native 카드 메뉴는 등록된 동일 패키지에서 TEST/정식을 명시적으로 선택할 수 있습니다. ExtensionCatalog.Download의 releaseChannel 인수는 기본 브랜치 descriptor 일치 검사를 먼저 통과한 뒤 다운로드할 채널만 변경합니다. SingleNativeButton.Resolve에는 선택한 채널을 전달하여 실제 ZIP 메타데이터와 대조합니다. 등록 기본값·ID·경로·asset은 변경하지 않으며, 새로고침의 정식→TEST 자동 업데이트 차단 정책도 유지합니다. 온라인 회귀 `dotnet run --project Manager.Tests -c Release -- --check-native-release-channels <임시 경로>`는 시트 저장소에서 정식→TEST→정식을 실제 다운로드하고 임시 선택 상태와 등록 기본값 보존을 검사합니다.

## pyRevit Reload와 독립 관리 버튼의 공존

pyRevit의 UI cleanup은 같은 pyRevit 탭에 있던 미갱신 Native 버튼/패널에도 Enabled=false를 설정할 수 있습니다. Manager 연결에서 Visible만 복원하면 버튼이 보이면서 회색으로 남습니다. Preview 26은 실제 설치/활성 선택에서 해석한 버튼에 한해 API Enabled=true를 복원하고, 소유 탭/목적지 패널도 활성화합니다. Revit 명령의 availability나 모델 편집 상태를 우회하지 않으며 다른 제품·미설치 버튼은 복원하지 않습니다. start마다 fingerprint/warmup을 초기화해 pyRevit UI cleanup이 끝난 뒤 Idling에서 다시 적용합니다.

revit-ui-state.json의 EnabledBeforeRestore, Enabled, UiEnabled, PanelEnabled는 복원 전후 상태를 기록합니다. tests/test-ribbon-activation.py는 실제 apply_layout 코드를 가짜 리본에 적용하여 두 관리 버튼과 부모 복원, 다른 제품/제거 버튼 보존, 미설치 탭 미생성, 반복 적용을 검사합니다. run-manager-tests.ps1에서 함께 실행합니다. pyRevit core와 Native Host DLL은 이 수정에 포함되지 않습니다.


## 저장소 이동과 단일 버튼 등록

신규 Revit 저장소는 중앙 목록 한 패키지·한 명령과 기능 descriptor가 일치해야 합니다. validate_registration.py는 두 번째 명령, 서로 다른 패키지 정의, 다른 저장소 ProjectReference, 부정확한 RoBIM 의존성 버전을 거부합니다.

catalog/modules.json의 repositoryMoves는 구 Native 저장소에 등록된 중앙 소유 패키지만 새 기본 저장소로 연결합니다. Registered()는 사용자 파일을 쓰지 않고 모르는 패키지·새 저장소의 사용자 브랜치 선택을 유지합니다. 시트의 일대일 이동은 Tool 설치 ID도 구 identity로 계산해 기존 레코드를 유지합니다. 이 이전 정의를 임의 제거하면 설치 ID가 달라지므로 호환 계약으로 보존합니다. 모듈/명령 ID는 저장소 이동으로 바꾸지 않습니다.

신규 소스 생성은 Workspace 도구를 사용하고 Manager와 Workspace PR을 함께 검토합니다. 중앙 목록 변경은 앱 소스 반영이며 사용자 PC는 새 목록이 포함된 Manager 배포 또는 URL 등록이 필요합니다. 새 저장소 자체는 자동 설치가 아닙니다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.

## 새로고침과 업데이트 표시

카드의 새로 고침은 로컬 설치 상태와 원격 기본 브랜치의 배포 정보·채널별 최신 버전을 확인합니다. 기존의 연녹색은 설치/선택됨, 회색은 미설치, 연주황색은 업데이트 가능을 나타냅니다. 정식 버전이 설치되어 있고 더 새로운 TEST가 제공될 때만 연보라색 카드와 TEST 보기 링크를 표시합니다. 정식 업데이트가 함께 있으면 연주황색을 우선하며, 미설치 도구나 TEST 설치 자체에 새 색상을 적용하지 않습니다. 업데이트가 있으면 버튼이 업데이트로 바뀌고 설치 버전 → 제공 버전을 표시합니다. 업데이트 먼저 정렬도 제공합니다. 업데이트를 누르면 검증된 패키지를 준비하고 실행 중인 Revit에 자동 적용을 요청합니다. CAD는 AutoCAD Reload를 사용합니다. 새로고침 자체는 패키지 다운로드·설치·모델 변경을 하지 않습니다.

시작·탭 전환·로컬 타이머는 네트워크를 조회하지 않습니다. 저장소 조회 실패 시 기존 설치와 마지막 확인 결과를 유지하고 최신 확인 실패를 표시합니다. 비교할 수 없는 외부/동봉 버전은 임의로 업데이트라고 판정하지 않습니다. Native는 실제 선택 모듈 버전, 기존 pyRevit은 선택 스냅샷의 브랜치·commit, CAD는 기록된 배포 버전을 비교합니다. 정식 설치를 TEST로 자동 전환하거나 새 버전을 자동 설치하지 않습니다.

Manager의 Revit 설치·업데이트·제거 및 리본 편집의 Revit 적용은 별도 Reload 클릭 없이 자동 적용을 요청합니다. Native Host는 세션 ID를 확인해 Idling에서 처리하고, 기존 pyRevit은 연결 확장의 ExternalEvent에서 적용·Reload합니다. 실행 중인 명령·모달 창이 끝나면 처리하며, 최초 Host 설치나 고정 로더/pyRevit 바이너리 교체는 기존 재시작 조건을 유지합니다. 요청 전송과 실제 applied 응답을 구분하고 다른 버튼의 캐시 업데이트는 자동으로 끌어오지 않습니다. 수동 Reload는 재시도용입니다.

## 정식 도구의 새 TEST 버전

새로 고침은 독립 Revit 도구의 정식 Release와 prerelease TEST를 한 번의 배포 목록 조회로 함께 확인합니다. 기본 배포가 정식인 저장소에도 더 높은 버전의 TEST가 있으면 정식 카드에 TEST 버전과 **TEST 보기** 링크를 표시하고 TEST 도구 목록에서도 같은 도구를 제공합니다. 기본 등록과 기존 정식 설치는 그대로 유지합니다. 이미 정식 버전에 포함된 오래된 TEST는 새 알림에서 제외하며, 설치된 TEST는 관리·제거를 위해 목록에 남습니다.

TEST 도구에서 **TEST 설치**를 누르고 확인하면 같은 버튼의 현재 버전을 TEST로 교체하고 Revit에 자동 적용을 요청합니다. 두 버전을 동시에 리본에 설치하는 기능은 아닙니다. 새 정식이 제공되면 **정식 업데이트**, 이전 정식으로 되돌릴 때는 ···의 저장소 정식 버전 설치를 사용합니다. TEST 자체의 업데이트는 내 도구와 TEST 목록에서 표시합니다. 새로고침이나 링크 클릭만으로 설치·선택·리본을 바꾸지 않습니다.

실패한 TEST 조회는 마지막 확인 결과와 실패 안내를 유지하며, 정상 조회에서 TEST가 없어지거나 정식 승격으로 이전 TEST가 포함되면 새 TEST 알림을 제거합니다. TEST의 검증 오류는 정상 정식 배포 조회를 막지 않습니다. 시작·탭 이동·타이머는 로컬 캐시만 읽습니다. 배포자는 정식 기본 channel을 유지하면서 새 TEST tag의 GitHub prerelease와 TEST manifest/ZIP을 게시할 수 있습니다.

## 내 도구의 정식·TEST·업데이트 상태

내 도구와 설치된 카드의 분류·버전은 저장소 기본 채널이 아니라 실제 선택한 모듈의 검증된 채널·버전을 사용합니다. 적용 준비 중이면 그 상태도 표시합니다.

| 실제 설치 | 제공 버전 | 표시와 기본 작업 |
| --- | --- | --- |
| 정식, 최신 | 같은 정식 | 정식 설치 · 제거 |
| 정식 | 더 새 TEST만 제공 | 정식 설치 + TEST 보기 · 연보라 안내, 자동 전환 없음 |
| TEST, 최신 | 기존 정식은 더 낮음 | TEST 설치 · 제거, 업데이트로 표시하지 않음 |
| TEST | 더 새 TEST | TEST 업데이트 · 설치 버전 → 제공 버전 |
| TEST | 설치 버전보다 새 정식/동일 본버전의 정식 승격 | 정식 업데이트 · 해당 정식 패키지 설치 |
| 정식 | 더 새 정식 | 업데이트 · 해당 정식 패키지 설치 |

정식에서 TEST를 선택 설치하면 같은 명령 ID의 설치 채널이 TEST로 바뀌며 내 도구와 TEST 목록에 반영됩니다. 정식 승격을 설치하면 다시 정식으로 표시합니다. 낮은 기존 정식으로 되돌리려면 ···의 저장소 정식 버전 설치를 명시적으로 선택합니다. 기본 업데이트 동작은 버전을 낮추지 않습니다. 새로고침 자체는 설치/채널/리본을 변경하지 않습니다.


## pyRevit 제거 후 예전 버튼 잔상

Reload 연결 1.0.0-preview.41은 파일이 제거된 RoBIM pyRevit 버튼이 독립 Add-in과 같은 패널에 남는 경우를 처리합니다. 선택되지 않은 예전 제어는 원래 내부 ID로 식별해 현재 세션에서 비활성·숨김 처리합니다. 파일 제거를 숨김으로 대신하지 않으며 선택된 pyRevit·독립 Add-in·다른 제품의 버튼은 유지합니다. RetiredPythonControls와 BridgeRevision=retirement-1을 적용 기록에 남깁니다. 연결 스크립트만 변경되므로 기존 백업·해시·세션 검증을 통과하면 Revit 실행 중 연결 업데이트와 표준 pyRevit Reload를 사용합니다. 실제 적용 완료는 새 세션 일치 응답과 기록을 확인합니다.

### 실제 적용 확인 — 2026-10-27

Revit 2026.5 실행 중 연결 1.0.0-preview.41을 설치하고 표준 pyRevit Reload 후 동일 세션의 applied 응답과 BridgeRevision=retirement-1을 확인했습니다. RetiredPythonControls에는 AutoJoin, WorksetDeleteAsCentral, Relink Paths, DuplicateViews, ViewRotation의 기존 내부 ID 5개가 기록됐습니다. 현재 선택된 pyRevit·Native 버튼은 유지했습니다. 실제 모델 수정 명령은 실행하지 않았습니다. 전체 핵심 검사 199개와 Python 리본 회귀 검사를 통과했습니다. 로컬 연결 적용과 전체 Manager 설치 프로그램 배포는 별도입니다.

## RoBIM pyRevit 사용 종료

RoBIM은 독립 Native 버튼으로 운영합니다. 기존 기능과 Python 리본 연결은 [Native 전환 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/PYREVIT_RETIREMENT.md)에 따라 백업·검증 후 제거합니다. NativeOnly에서는 Host가 리본/Reload를 담당하며 pyRevit 본체·다른 확장은 유지합니다. 고정 Host 교체는 Revit 종료가 필요합니다. 새 PC 적용·재시작 후 검증 전에는 완료로 기록하지 않습니다.


## NativeOnly 연결 호환성 — Manager 1.0.0-preview.43

동봉 Native Host는 1.0.0-preview.4입니다. preview.3에서 발견한 UI JSON 키 대소문자 문제를 수정한 Host부터 RoBIM pyRevit 제거의 필수 조건을 충족합니다. 기존 Manager UI 연결 계약은 유지합니다. 종료 후 정상 Setup 설치와 재시작 세션의 실제 applied 응답을 별도로 검증합니다. 이전 TEST 배포 파일은 교체하지 않습니다.


2026-10-27 실제 PC 적용: 정상 Windows Setup으로 Manager 1.0.0-preview.43 및 Host 1.0.0-preview.4를 설치했습니다. Revit 재시작 후 Native Provider, 독립 기능 8개·관리 버튼 2개를 확인했습니다. Manager.Core의 RequestRevit(false)가 생성한 요청 ID와 Host의 applied 응답이 일치했고, 응답 시각이 안정적으로 유지되어 반복 적용이 없음을 확인했습니다. RoBIM Python 확장 0개·다른 확장 9개 및 기존 Native 선택을 보존했습니다. 모델을 열거나 모델 명령을 실행하는 검증은 수행하지 않았으며 TEST 배포입니다. 기본 브랜치 병합은 수행하지 않았습니다.


## GitHub 팀 인증

[TEAM_ACCESS.md](TEAM_ACCESS.md): 고정 팀 정책, 자동 로그인, 알림 영역/Windows 자동 시작, Host/CAD 실행 차단 및 검증.

## preview.48 계정 설정

ManagerAccountSettings.cs의 두 접이식 영역으로 계정 변경과 고급 설정을 분리합니다. GitHubSession/TeamAccess 인증·자동 확인·자격 증명 저장 정책은 유지합니다. 기본 화면의 저장된 연결 확인 버튼과 이전 pyRevit Reload 안내를 제거했습니다. --verify-account-ui는 기본 숨김, 계정 변경 열기/닫기, 고급 설정, 닫을 때 토큰 입력 지우기 6개를 검사하며 --verify-team-ui 7개 회귀 검사도 통과했습니다. 실제 설치 여부는 적용 기록을 확인합니다.


## 팀별 정식·TEST 권한 — 2026-10-28

Manager 1.0.0-preview.49 / Revit Host 1.0.0-preview.7 / CAD Loader 1.0.1-test.3 / RoBIM.Access.Core [0.1.0-test.4]부터 적용합니다. 이전의 robim 단일 팀 정책보다 이 정책이 우선합니다.

| GitHub 팀 | Manager와 지원 호스트에서 허용하는 도구 |
| --- | --- |
| RoBIM-tech/robim | 정식(stable) |
| RoBIM-tech/robim-test | 정식 + TEST |
| RoBIM-tech/robim-dev | 정식 + TEST; 저장소 쓰기 권한은 개별 저장소에서 별도로 부여 |
| RoBIM-tech/robim-admin | 정식 + TEST; 관리자 구성원은 gagaggan |

네 팀 중 활성 멤버십 하나가 있어야 사용합니다. 여러 팀 소속이면 admin → dev → test → 일반 사용자 순으로 표시합니다. 상위 역할은 robim 팀의 중복 가입을 요구하지 않습니다. GitHub 초대 수락 전의 pending 상태는 허용하지 않습니다. 저장소 Admin 권한과 앱의 팀 역할은 별개입니다.

일반 사용자는 TEST 메뉴·권유·설치·다운로드·실행을 사용할 수 없습니다. 같은 도구의 검증된 정식 Release가 있으면 정식 버전을 제시합니다. 기존 TEST 설치의 제거는 허용하며 TEST 캐시를 정식으로 재표시하지 않습니다. 알 수 없는 배포 구분은 관리자도 실행하지 못합니다. 패키지 내부의 실제 모듈별 배포 정보를 확인하며 카드 문구만으로 허용하지 않습니다.

Manager는 시작/2분 주기 GitHub 멤버십 조회, 최대 3분 유효 상태를 유지합니다. TEST 팀 조회 실패는 TEST를 허용하지 않지만 별도로 확인한 robim 정식 권한은 유지합니다. 로그아웃·종료·권한 만료는 실행을 잠급니다. 현재 사용자 전용 Named Pipe v2는 stable/test 비트만 전달하고 토큰을 호스트에 넘기지 않습니다. Host/Loader는 1초마다 조회하고 3초 만료 및 명령 실행 직전 재검사를 적용합니다. 구형 v1 호스트는 새 Manager에서 권한을 받을 수 없으므로 세 제품을 함께 업데이트하고 Revit/CAD를 재시작해야 합니다.

로그인 전 첫 화면과 자동 로그인/시작 정책은 유지합니다. 로그인 전에도 정확한 Reload와 Manager 열기 명령은 사용할 수 있습니다. 실제 기능 명령에는 이 예외를 적용하지 않습니다. 사용자 리본·도구 선택·기존 설치 ID는 유지합니다.

이는 Manager와 정식 Host/Loader가 적용하는 권한입니다. GitHub는 같은 저장소의 Release별 다운로드 권한을 나누지 않으므로 저장소 Read 구성원은 GitHub에서 직접 TEST 파일을 받을 수 있습니다. 중계 서버는 도입하지 않았으며 변조된 실행 파일이나 직접 로드까지 통제한다고 설명하지 않습니다.

검증: Manager 엔진 233개, 정상 Windows 역할 UI 19개·계정 UI 6개·Named Pipe 14개가 통과했습니다. SDK 빌드와 실제 Revit/CAD 모델 명령 검증은 구분합니다. 실제 일반 사용자 계정 및 모델/DWG에서의 실행 검증은 미수행입니다. 기본 브랜치 병합과 정식 승격은 별도입니다.

[팀별 권한 전달 기록](TEAM_ROLE_DELIVERY_20261028.md): 새 TEST 배포·검증·현재 PC 설치와 남은 호스트 적용 단계.


## 자동 결합 모델리스 TEST 전달 · Manager 1.0.0-preview.50

Revit Host 1.0.0-preview.8을 동봉해 자동 결합 0.1.0-test.2의 모델리스 창과 행 선택을 지원합니다. Manager 기능 로직·권한/로그인·CAD 패키지·사용자 선택/리본은 변경하지 않습니다. Host 교체에는 Revit 종료·재시작이 필요합니다. 새 Manager 설치 후 자동 결합 카드를 새로고침하고 TEST 업데이트를 설치합니다. 창에서 뷰를 바꾸거나 모델을 수정해도 결과표를 유지합니다.

로컬 빌드/임시 설치 검증과 실제 PC 설치·Revit 모델 명령 검증은 구분합니다. 배포 근거는 docs/developer/MODELESS_TEST_DELIVERY_20261028.md에 기록합니다.


## preview.51 · Git 없는 로그인과 .NET 확인

GitHub로 로그인을 누르면 인증 코드와 GitHub 인증 페이지가 표시됩니다. 코드 입력·앱 승인 후 연결 계정을 직접 확인하고 Windows 자격 증명 관리자에 Manager 전용 인증을 저장합니다. Git/GCM 설치는 필요하지 않습니다. 기존 Git/GCM 연결 계정은 한 번 다시 로그인해야 합니다. 만료·철회된 인증은 재로그인하며 자동 시작은 브라우저를 열지 않습니다. 계정 목록도 Manager 전용 저장소에서 읽고 공용 GitHub 인증은 변경하지 않습니다. 이전 Git 작업 사본을 관리하는 고급 레거시 기능은 여전히 Git을 필요로 합니다.

Setup은 설치 전에 .NET 실행 환경을 자동 검사합니다. 별도 확인 버튼을 두지 않습니다. Manager는 .NET을 포함해 배포하므로 별도 .NET 설치가 필요 없습니다. Revit 2026.5+/AutoCAD 2026.1.2+의 runtimeconfig와 x64 .NET/Windows Desktop/호스트가 요구하는 ASP.NET Core 실행 환경을 검사합니다. 다른 .NET 버전이나 x86 설치만으로 충족 처리하지 않습니다. 기능 설치 전에도 해당 호스트의 실행 환경을 확인합니다. 등록된 설치 경로와 C:\Revit 2026 같은 별도 경로를 지원하고 찾지 못하면 설치 폴더를 지정합니다. 호스트가 .NET 8인 경우와 .NET 10 x64 런타임 누락을 구분하여 안내합니다. SDK는 사용자 설치 요건이 아닙니다.

정식 전환 대상은 시트 생성·파라미터 복사·중앙파일 복제/워크셋 수정·링크 경로 변경·뷰 복제 및 CAD 전체입니다. 사용자 gagaggan은 최신 TEST 실사용을 Revit 26.5.0.55/AutoCAD R25.1.179.0에서 검증했다고 확인했습니다. 배포/Manager 새로고침/설치/실제 호스트 적용은 각각 확인합니다.


preview.51은 Setup 설치본의 Manager 자체 자동 업데이트를 제공합니다. 로그인 후 6시간 간격으로 Manager Release만 확인하고 새 버전의 고정 Setup asset을 GitHub SHA-256 digest로 검사합니다. 정식 Manager는 TEST로 자동 전환하지 않으며 현재 preview 계열은 후속 preview와 정식 승격을 받을 수 있습니다. 설정에서 자동 업데이트를 끌 수 있습니다. 도구 선택/기능 버전은 자동 변경하지 않습니다. 작업 중이거나 Revit·AutoCAD가 실행 중이면 적용을 대기하며 종료 후 설치 프로그램으로 교체·재시작합니다. Portable은 자동 덮어쓰지 않습니다. 설치 로그와 업데이트 결과는 Manager 데이터의 ManagerUpdates 하위에 기록합니다.

Navisworks 메뉴는 등록 도구가 없는 빈 목록과 기본 경로 설치 버전을 제공합니다. Navisworks 플러그인 패키지 설치·실행 지원을 완료한 것은 아닙니다. .NET 8 호스트 지원은 이번 범위에서 제외합니다.


preview.52: 도구 메뉴와 페이지 제목의 Revit 독립 Add-in을 Revit으로 표시합니다. 내부 모듈·명령·설치 ID와 동작은 유지합니다.


프로그램별 리본 편집: 왼쪽 Revit·AutoCAD·Navisworks 아래에 각각 리본 편집을 둡니다. Revit은 기존 탭/패널/묶음 편집과 자동 적용을, AutoCAD는 기존 CAD 구성의 버튼 이름/패널/순서와 CAD Reload 준비를 사용합니다. CAD 구성을 Revit 리본 파일에 저장하거나 Revit 적용을 호출하지 않습니다. Navisworks는 등록 도구가 없어 편집할 버튼이 없음을 표시합니다. Navisworks 저장·호스트 적용을 지원한다고 표시하지 않습니다.


설정의 Manager 업데이트 확인은 현재/최신 버전 정보만 확인합니다. 새 버전이 있으면 Manager 업데이트 버튼을 표시하며 사용자가 선택하면 검증한 Setup을 준비합니다. 자동 업데이트를 꺼도 수동 확인·설치를 사용할 수 있고, Portable은 배포 페이지를 엽니다.


리본 결과 확인은 현재 Revit PID/시작 시각의 요청 ID와 일치한 응답만 적용 완료로 표시합니다. 이전 세션·이전 요청의 applied는 현재 성공으로 표시하지 않습니다. TEST/정식 전환은 같은 native module/command ID의 저장 배치를 보존해야 하며 별도 실제 호스트 적용 확인이 필요합니다.

## Preview 53: Revit 리본 연결 복구

리본 편집 위의 **Revit 연결 설치 / 업데이트**는 독립 Revit 연결을 설치하거나 Manager에 동봉된 검증 버전으로 갱신합니다. Revit에서 작업을 저장하고 종료한 뒤 실행하고, 다시 Revit을 시작하면 저장한 배치를 자동 적용합니다. 연결을 갱신해도 도구 선택과 리본 배치는 유지합니다.

독립 모드의 Revit 적용은 설치된 연결과 동봉 패키지의 실제 파일 해시를 비교합니다. 연결이 오래된 채로 Revit이 실행 중이면 적용 완료로 표시하지 않고 연결 업데이트 절차를 안내합니다. Revit이 종료되어 있으면 적용 요청 과정에서 연결을 먼저 설치/갱신합니다. 실행 중 Revit의 연결 DLL을 교체하거나 Revit을 강제 종료하지 않습니다.

## Preview 54: Native 전용 Manager 업데이트

Revit 도구·검색·새 설치·리본은 독립 C# Add-in만 사용합니다. pyRevit 메뉴와 연결 신규 설치는 제공하지 않으며, 기존 소유 잔여 설치가 있으면 설정의 고급 항목에서 백업 후 제거할 수 있습니다. 업데이트 자체는 이전 pyRevit 파일이나 사용자 리본을 자동 삭제하지 않습니다. Revit 리본 연결 업데이트와 Manager 업데이트 확인 버튼을 유지합니다.

AutoCAD 화면의 호환성/선택 오류와 비동기 화면 오류는 로컬 cad-panel-errors.log에 기록합니다. Navisworks는 빈 도구/리본 안내를 유지합니다. Manager의 미리보기 예시도 Native 도구로 바꾸어 제거된 pyRevit 제품을 찾다가 실패하지 않게 했습니다.

### Native 전용 검사 범위

Manager.Tests는 Native/CAD·인증·Manager 업데이트·설치 보존·리본·잔여 설치 제거를 검사합니다. retired-pyrevit-tests.json의 과거 pyRevit 신규 설치/프로필/Reload 성공 시나리오는 현재 운영에서 종료되어 기본 실행에서 제외하며 통과 수에 포함하지 않습니다. 별도 retired-tests.json에 이름과 이유를 기록합니다. 과거 동작을 조사할 때만 --include-retired-pyrevit을 사용하며 현재 정책에서 실패하는 것이 정상입니다. 기존 설치의 제거/백업/동시 변경 방지 검사는 구형 설치 기록을 직접 준비하여 계속 실행합니다. NativeOnlyPolicyTests는 실제 Manager.Core를 대상으로 신규 pyRevit 설치/등록 차단 및 기존 파일 보존을 검사합니다.
