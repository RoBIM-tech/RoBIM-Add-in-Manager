---
name: robim-manager
description: RoBIM Add-in Manager의 버튼 카드·선택 설치·설치 위치·리본 편집·버전/패키지·Reload·Windows 배포를 개발하거나 진단한다. 기능 알고리즘은 각 도구 저장소에서 수정한다.
---

# RoBIM Add-in Manager

이 저장소는 Manager.Core, RoBIM.Manager, 설치 프로그램과 Manager 소유 pyRevit 연결을 소유한다. README.md, docs/USER_GUIDE_KO.md, AGENTS.md 및 변경 영역의 구조 문서를 읽는다. Windows x64 / .NET 10이다.

## 기능과 설치 정책

- preview.44 제거는 확인 후 ManagerUninstall을 오프라인 실행하여 관리 도구·Host 등록·검색 캐시·백업을 정리하고 실행 파일을 제거한다. 개인 설정 삭제/유지를 선택하며 유지 시 계정 연결·검색 대상·리본·설치 위치·모드를 보존한다. 이전 검색 결과/설치 기록은 남기지 않는다. 실행 중 호스트는 재시도/취소로 대기하며 강제 종료/무시를 제공하지 않는다. 실패 시 Manager를 남긴다. 외부 도구·공용 GitHub 로그인은 삭제하지 않는다. 무인 제거 기본은 설정 삭제이고 /KEEPSETTINGS=1은 유지다. 테스트 Setup은 실제 사용자 데이터 정리를 호출하지 않는다. docs/developer/UNINSTALL.md와 ManagerUninstallTests를 확인한다.

- 독립 Revit Add-in, Revit · pyRevit, AutoCAD 목록을 분리한다. 카드 단위는 실제 명령이며 같은 패키지의 다른 명령 선택·의존성을 보존한다.
- 미설치/준비됨/실제 적용됨을 구분한다. Native Host와 CAD Loader의 세션 PID·시작 시각·applied 응답을 확인한다. Revit 리본 API의 숨김과 실제 설치 명령 파일 제거를 같은 의미로 설명하지 않는다.
- 외부 설치·사용자 변경은 소유권과 해시 검증 없이 덮어쓰거나 삭제하지 않는다. 일반 설치·고정 로더 교체는 호스트 종료, 호환되는 기능/스크립트 변경은 준비 후 해당 Reload 정책을 따른다.
- pyRevit 사용자 설치 위치는 선택 프로필과 개발 프로필에 적용하며 기존 검색 경로를 보존한다. Manager 연결 확장은 기본 검색 위치에 둔다. pyRevit core를 패치하지 않는다.
- 리본의 큰 버튼은 한 칸, 작은 버튼은 한 칸의 2/3단 묶음이다. 구분선·드래그 앞/뒤 삽입·탭 호버·이름 입력 좌표는 실제 WinForms 이벤트와 모델 저장을 함께 검사한다. 미리보기 편집은 설치를 대신하지 않는다.
- 시작·탭 전환에서 GitHub를 반복 조회하지 않는다. 캐시는 초기 표시용이며 설치·제거·적용의 검증을 생략하는 근거로 쓰지 않는다.

## 경로·배포·검증

- WindowsDataPath의 실제 파일 핸들 경로 검증을 유지한다. Codex MSIX 실행 환경에서 논리 LocalAppData가 앱 전용 LocalCache로 연결될 수 있다. 일반 사용자 조건의 설치·실행·Native Host 등록을 따로 확인한다.
- Native Host 구현은 독립 Add-in 저장소, CAD Loader 구현은 CAD 저장소에 둔다. NativeProtocol.cs와 CadConfiguration.cs의 컴파일용 계약 사본은 통합 검사로 동기화한다.
- Manager는 검증된 Release ZIP/SHA-256을 받으며 다른 저장소의 빌드/설치 스크립트를 실행하지 않는다. 계정 정보는 Windows 자격 증명 관리자를 사용하고 INI·로그·Git에 토큰을 기록하지 않는다.
- tests/run-manager-tests.ps1, tests/test-manager-installer.ps1 및 변경한 화면의 --verify-card-ui를 사용한다. 테스트는 임시 경로에 설치한다. 개발·설치 캐시와 실제 사용자 데이터 경로를 혼동하지 않는다.
- build-manager.ps1은 검증된 CAD/Host 패키지와 런타임 라이선스를 동봉한다. 작업본 검증 버전과 GitHub Release 게시 버전을 구분하고 최종 실행 파일 해시를 확인한다.

## 신규 제작·공용 코드·배포 구분

- 신규 Revit 기능은 독립 Native Add-in으로 만든다. pyRevit 신규 버튼은 추가하지 않고 기존 기능·Manager 연결을 유지보수한다.
- 공용 .NET 원본은 RoBIM-Libraries에서 역할별 패키지로 관리하고 소비 버전을 정확히 고정한다. 기능 내부 DLL 사본과 Host 경계 공유 타입을 구분한다.
- 신규 버튼은 robim-addon 토픽·기본 브랜치 robim-package.json(moduleId/commandId)·TEST Release로 자체 배포 정보를 제공한다. Manager 제작자·조직/연결 계정 범위와 접근 권한을 확인하고 새로고침의 실제 카드를 검증한다. 중앙 목록은 기본 추천·이전 호환용이며 신규 표시를 위한 필수 PR/Manager 재배포를 요구하지 않는다.
- 새 배포는 TEST이며 Manager TEST 도구에 표시한다. 실사용 승격은 새 정식 버전과 실제 호스트 검증자·빌드·HTTPS 기록이 필요하다. 버전명·브랜치·설치 성공만으로 승격하지 않는다.
- 신규 제작 시 통합 저장소 docs/developer/NEW_BUTTON.md와 scripts/new_revit_module.py를 읽는다. 생성 도구는 Manager 없이도 배포 표시 정보·검증기·설명서·전용 스킬을 만들며 중앙 등록은 선택 사항이다.


## 버튼별 저장소 운영

신규 Revit 버튼은 **버튼 하나 = Git 저장소 하나 = Native 모듈 하나 = 설치 패키지 하나**로 운영합니다. 다른 버튼은 새 저장소에서 만들며 버튼별 장기 브랜치로 나누지 않습니다. codex/ 브랜치는 해당 저장소 안의 수정 작업용입니다. 공유 구현은 RoBIM-Libraries의 역할별 NuGet으로 제공하고 소비 버전을 정확히 고정합니다. 공용 Host/Loader/Manager를 버튼마다 복제하지 않습니다.

모든 새 버튼은 자체 검색 메타데이터와 Workspace 소유권을 기록하고 TEST부터 시작합니다. README에는 일반 사용자/개발자 설명서와 전용 스킬을 연결합니다. 기존 CAD는 유지하고 pyRevit 저장소는 이력만 보관이며 새 Native 버튼을 추가하는 장소가 아닙니다.

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 검색되거나 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.

## 버튼 제작 기여 확인

새 버튼 또는 배포/등록 변경 시 [버튼 제작 계약](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_CONTRACT.md)을 읽는다. 표시 이름과 안정된 ID, 패키지와 자체 배포 표시 정보의 일치(중앙 목록에 기여할 때는 그 항목도 일치), 고정 NuGet 버전별 공급원, 일반 사용자/개발자 설명서, TEST Release와 정식 승격 후 PC 적용을 함께 확인한다. 병합을 새 Release 게시나 사용자 설치 허가로 해석하지 않는다. 기존 기능 유지보수 저장소에 새 버튼을 넣지 않는다.

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


## 매번 적용할 제작 출발점

신규 버튼 전 Workspace START_HERE.md → NEW_BUTTON.md → BUTTON_CONTRACT.md → Libraries 공통 UI 기준/API를 읽고, 기존 버튼은 해당 README·AGENTS·전용 스킬·UI_ADOPTION도 확인한다. 생성기를 사용하고 사용한 기준·실제 버전·예외를 기록한다. 기본 브랜치/작업 브랜치/Release/PC 적용을 구분한다. 완료 보고에는 사용자·개발자 문서·스킬 갱신과 Manager 실제 카드·설치/적용 단계의 검증 결과를 포함한다. 파일 존재나 PR 생성만으로 완료라고 하지 않는다.


## 관리 리본 편집

리본 편집에서 설치한 기능과 함께 pyRevit Reload, 독립 Add-in Reload, Add-in Manager를 배치합니다. 관리 패널 이름·탭·버튼 위치·표시 이름·2단/3단 묶음도 일반 버튼처럼 수정하고 저장한 뒤 Revit 적용을 누릅니다. 기존 관리 버튼 배치는 보존하며 새로 포함된 관리 버튼만 기본 위치에 보완합니다. 해당 종류의 기능이 하나도 설치되지 않으면 관리 버튼만으로 빈 탭을 만들지 않습니다. 관리 버튼의 배치 삭제는 연결 모듈 제거가 아니며, 미배치 상태에서는 기본 위치로 표시됩니다.

기본 패널이 비어 있는 기능은 RoBIM 도구 → 도구에 배치합니다. 관리 탭을 빈 패널의 대체 위치로 사용하지 않습니다. 기능 패키지가 관리 탭/관리 패널을 기본값으로 제시해도 자동 배치에서는 도구 영역으로 분리하며, 사용자가 명시적으로 저장한 배치를 우선합니다. 명시적으로 저장한 사용자 위치는 자동으로 옮기지 않습니다. Native 패키지는 유효한 기본 tab/panel을 선언해야 하며 제작 단계부터 기능 패널을 지정합니다.


Manager 선택 설치는 선택하지 않은 pyRevit 명령과 탭·패널·버튼 묶음 폴더를 활성 확장에서 실제로 제거합니다. sentinel layout만으로 숨기는 방식에 의존하지 않습니다. 선택한 버튼의 부모 컨테이너와 공용 lib는 보존하며 전체 원본은 검증된 별도 캐시에 남겨 재설치합니다. 선택 0개라면 활성 .tab 폴더도 남기지 않습니다. 기존 설치 정리는 선택/보류 상태와 소유권·해시를 확인하고 백업 후 적용합니다. 이미 생성된 Revit 리본 컨트롤은 Reload/다음 시작에서 정리되며 파일 제거와 현재 세션 UI 제거를 구분합니다.


## Preview 38: 묶음 버튼 연결과 제한된 재시도

pyRevit 묶음 안의 선택 버튼이 RibbonPanel.GetItems에서 빠지는 경우, 원본 탭·패널·명령의 내부 control ID로 AdWindows 트리를 확인합니다. 표시 이름으로 유사한 버튼을 추측하지 않으며 동일 ID가 여러 개면 적용을 중단합니다. 실패한 시작 적용은 2/4/8초 간격으로 최대 네 번 시도하고 같은 오류는 한 번만 기록합니다. 새 리본 요청이나 원본/선택/Native 배포 변경, 표준 pyRevit Reload로 다시 시도할 수 있습니다. Missing/프로세스 ID/조회 진단과 재시도 중지 상태를 Manager 데이터 폴더에 남깁니다. 성공한 재시도는 이전 failed 응답을 applied로 갱신합니다.

기존 Manager 소유 연결의 스크립트·이미지 변경은 InstallReload의 파일별 검증·백업과 Revit 세션 재확인을 거쳐 적용하고 표준 pyRevit Reload를 요청합니다. 준비된 pyRevit 선택/프로필이 있으면 먼저 적용하도록 차단하며 DLL 등 바이너리 변경은 실행 중 교체하지 않습니다. 자동 적용은 Revit 한 개가 실행 중일 때 지원합니다. 파일 교체와 실제 실행 세션의 적용 응답을 구분합니다.


Preview 39는 pyRevit이 선택된 단독 stack 버튼 자체를 생성하지 못한 경우도 처리합니다. 현재 pyRevit 세션에 이미 로드된 정확한 IExternalCommand·availability 클래스를 선택된 원본 경로로 확인한 뒤 기존 소스 패널에 리본 버튼만 복구합니다. 다른 DLL을 로드하거나 기능 스크립트·모델을 실행하지 않습니다. 동일 이름 API 버튼이 있으면 중복 생성하지 않고, 클래스/패널이 불명확하면 진단을 남깁니다. 폴더 이동·명령 ID·사용자 배치는 변경하지 않습니다.


Preview 40은 Python 쪽 unique name과 네이티브 pyRevit 파서가 만든 클래스명에서 MINUS/minus 같은 토큰 대소문자가 다른 경우를 CLR의 모호성 검사로 안전하게 처리합니다. 실제 타입의 FullName을 리본과 availability에 사용합니다. 성공한 적용은 이전 오류 진단도 resolved로 갱신합니다. 실제 Revit에서 Relink Paths를 RoBIM → Insert에 복구하고 누락 0개/14개 버튼 applied 응답을 확인했습니다. 기능 실행이나 모델 쓰기 검증과는 구분합니다.


## 제작·수정 공통 확인

신규 버튼과 기존 수정 모두 [명명 기준](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NAMING.md)과 [전달 완료 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_DELIVERY.md)를 먼저 읽는다. 화면 이름은 기능명, 공용 패키지는 RoBIM. 전체 이름을 사용하고 기존 설치/모듈/명령 ID는 유지한다. 사용 가능한 버튼 전달 요청은 로컬 코드 작성에서 멈추지 않고 요청 범위의 저장소 게시·TEST 배포·Manager 검색/설치·호스트 적용까지 진행한다. 신규 검색은 robim-addon 토픽·기본 브랜치 descriptor(moduleId/commandId)·Release·검색 대상/권한으로 확인하며 중앙 등록 PR/Manager 재배포는 필수가 아니다. 이 절차가 오래된 중앙 등록 필수 문구보다 우선한다. README·사용자/개발자 문서·스킬·UI_ADOPTION을 실제 변경에 맞춰 갱신하고 소스/기본 브랜치/Release/Manager 카드/설치/호스트 결과를 구분해 보고한다. 게시·병합·설치는 현재 요청 범위를 따르며 미수행 단계를 완료라고 기록하지 않는다.


## pyRevit 제거 후 예전 버튼 잔상

Reload 연결 1.0.0-preview.41은 파일이 제거된 RoBIM pyRevit 버튼이 독립 Add-in과 같은 패널에 남는 경우를 처리합니다. 선택되지 않은 예전 제어는 원래 내부 ID로 식별해 현재 세션에서 비활성·숨김 처리합니다. 파일 제거를 숨김으로 대신하지 않으며 선택된 pyRevit·독립 Add-in·다른 제품의 버튼은 유지합니다. RetiredPythonControls와 BridgeRevision=retirement-1을 적용 기록에 남깁니다. 연결 스크립트만 변경되므로 기존 백업·해시·세션 검증을 통과하면 Revit 실행 중 연결 업데이트와 표준 pyRevit Reload를 사용합니다. 실제 적용 완료는 새 세션 일치 응답과 기록을 확인합니다.


## RoBIM pyRevit 사용 종료

RoBIM은 독립 Native 버튼으로 운영합니다. 기존 기능과 Python 리본 연결은 [Native 전환 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/PYREVIT_RETIREMENT.md)에 따라 백업·검증 후 제거합니다. NativeOnly에서는 Host가 리본/Reload를 담당하며 pyRevit 본체·다른 확장은 유지합니다. 고정 Host 교체는 Revit 종료가 필요합니다. 새 PC 적용·재시작 후 검증 전에는 완료로 기록하지 않습니다.


## NativeOnly 연결 호환성 — Manager 1.0.0-preview.43

동봉 Native Host는 1.0.0-preview.4입니다. preview.3에서 발견한 UI JSON 키 대소문자 문제를 수정한 Host부터 RoBIM pyRevit 제거의 필수 조건을 충족합니다. 기존 Manager UI 연결 계약은 유지합니다. 종료 후 정상 Setup 설치와 재시작 세션의 실제 applied 응답을 별도로 검증합니다. 이전 TEST 배포 파일은 교체하지 않습니다.


## GitHub 팀 인증

preview.45: TEAM_ACCESS.md를 읽는다. RoBIM-tech/robim active 구성원만 허용하며 TeamAccess의 주기 검증과 만료를 우회하지 않는다. RoBIM.Access.Core [0.1.0-test.3]을 고정한다. 로그인/로그아웃/자동 시작·알림 영역은 Manager 소유이며 Host/CAD 실행 검사는 각 저장소 소유다. GitHub 토큰을 호스트·로그·설정 파일에 전달하지 않는다. 로그인 진입/종료/제거는 잠그지 않고 다른 제품은 제한하지 않는다. 고정 Host/Loader는 종료 후 새 TEST 패키지와 재시작 검증이 필요하다.


## preview.46 로그인/Reload 예외

로그인 전에는 전용 첫 화면만 표시하고 메뉴·도구 목록을 숨긴다. 인증 성공은 도구 홈, 로그아웃/실패는 로그인 화면으로 전환한다. Host6/CAD Loader test.2의 정확한 Reload/Manager 버튼만 로그인 전 허용한다. 실제 기능 실행의 인증 검사를 제거하거나 관리 버튼 전체를 풀지 않는다. 기존 preview.45의 Reload 잠금 설명보다 이 정책을 우선한다.


## 조직 이전 — preview.47

ORGANIZATION_TRANSFER.md를 읽는다. 현재 소유자는 RoBIM-tech이며 15개 이전 저장소만 RepositoryOwnership로 정규화한다. 도구 레코드 ID는 이전 gagaggan namespace와 기존 기능 분리 alias를 유지한다. 원격 리포지토리 주소와 인증 계정명을 혼동하지 않는다. 과거 소유자 계정명은 인증 ID/이전 호환 키로 보존한다. 새 조직 검색·현재 설치·원격 main/Release/연결 앱 승인을 별개로 검증한다. 미커밋 작업을 일괄 게시하거나 과거 Release 파일을 교체하지 않는다.

## preview.48 계정 설정

기본 화면에는 현재 계정과 자동 로그인/시작, 계정 변경/로그아웃만 제공합니다. 저장 계정·브라우저 계정 변경은 접힌 계정 변경 영역, PAT·작업 기록·이전 pyRevit 관리는 접힌 고급 설정에서만 표시합니다. NativeOnly에서는 기존 pyRevit 관리를 숨깁니다. 인증/주기 확인 정책을 바꾸지 않으며 --verify-account-ui와 --verify-team-ui로 화면 전환을 검증합니다.


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

## 신규 개발자 온보딩과 현재 계약 (2026-10-28)

- 처음 참여하면 https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/START_HERE.md 에서 환경 준비부터 확인합니다.
- 신규 Revit 버튼은 자체 robim-package.json(moduleId/commandId)·robim-addon 토픽·TEST Release·GitHub 접근 권한으로 검색합니다. Manager 중앙 카탈로그는 기존 등록/추천/이전 호환 경로이며 신규 버튼에 필수는 아닙니다.
- 기존 영구 ID·설치·사용자 리본·정식/TEST 채널을 보존하고 검색, ZIP 검증, 설치 준비, 실제 Host applied 및 모델 실행을 각기 검증합니다.

## Native-only 운영 전환 (2026-10-28)

- RoBIM은 pyRevit을 사용하지 않습니다. 기존 `RoBIM-pyrevit`, `RoBIM-pyrevit-extension` GitHub 저장소는 보관하며 신규 검색·리본·설치·TEST·개발 스킬의 운영 대상에 포함하지 않습니다.
- 이전 pyRevit 로컬 설치·백업·사용자 설정을 자동으로 삭제하지 않습니다. Native Revit Host와 독립 버튼만 사용합니다.
- CAD 패널 오류가 발생하면 Manager 데이터 폴더의 `cad-panel-errors.log`, AutoCAD의 `%LOCALAPPDATA%\\RoBIM\\Cad\\ribbon-errors.log`를 확인합니다. 권한 서비스 장애 시 CAD 리본 명령은 안전하게 비활성화합니다.

## Preview 54: Native 전용 Manager 업데이트

Revit 도구·검색·새 설치·리본은 독립 C# Add-in만 사용합니다. pyRevit 메뉴와 연결 신규 설치는 제공하지 않으며, 기존 소유 잔여 설치가 있으면 설정의 고급 항목에서 백업 후 제거할 수 있습니다. 업데이트 자체는 이전 pyRevit 파일이나 사용자 리본을 자동 삭제하지 않습니다. Revit 리본 연결 업데이트와 Manager 업데이트 확인 버튼을 유지합니다.

AutoCAD 화면의 호환성/선택 오류와 비동기 화면 오류는 로컬 cad-panel-errors.log에 기록합니다. Navisworks는 빈 도구/리본 안내를 유지합니다. Manager의 미리보기 예시도 Native 도구로 바꾸어 제거된 pyRevit 제품을 찾다가 실패하지 않게 했습니다.

## Preview 55: 동봉 Revit 연결 패키지 수정

preview.54 이하의 일부 Manager 배포는 동봉 Host를 preview.8로 문서화했지만 실제 ZIP은 preview.1이었습니다. 로컬 SHA-256과 sidecar만 일치해도 빌드가 허용된 것이 원인입니다. preview.55는 GitHub 공식 host-v1.0.0-preview.8의 ZIP 해시 e4371baf…로 고정하고 실제 DLL 버전 preview.8을 확인했습니다. CAD stable1.0.1은 기존 공식 배포와 일치했습니다.

release-packages.lock.json이 동봉 버전·공식 tag·ZIP 해시를 기록하며, 빌드는 구형 Host ZIP과 그에 맞는 로컬 sidecar 조합도 거부합니다. 정식 Setup 빌드의 CAD도 고정하며 Portable 후보 빌드만 명시적 CAD 후보를 허용합니다. 현재 Host 교체는 Revit 종료 후 ‘Revit 연결 설치 / 업데이트’로 진행하고, 저장된 리본·도구 선택을 보존합니다. 업데이트가 실행 중 Revit을 강제 종료하지 않습니다.

## Manager preview.56 · Host preview.9

TEST 빌드는 검증된 Revit Host 1.0.0-preview.9 ZIP과 SHA-256을 동봉합니다. 고정 Host 갱신에는 Revit 종료가 필요합니다. 기존 관리 기록과 실제 파일이 다르면 덮어쓰지 않으며, 실제 설치·적용 여부는 별도 확인합니다.

## Cross-host ribbon TEST

AutoCAD and Navisworks use separate visual ribbon layouts. AutoCAD layout edits are saved independently; applying prepares the existing selection for ROBIMRELOAD. A saved layout does not itself mean the host applied it. Navisworks host rendering is not implemented. Manager prerelease update opt-in defaults off; version ordering still prevents downgrades. CAD host UI and model-command verification remain pending.

## 프로그램 로더 관리

프로그램 로더 페이지에서 RoBIM CAD 로더(RoBIM-tech/RoBIM-CAD)와 RoBIM Revit Host 로더(RoBIM-tech/RoBIM-Revit-Addin)의 설치 경로와 동봉 DLL 일치 여부를 확인하고 적용할 수 있습니다. 로더 적용 전 해당 호스트를 종료해야 합니다. 관리되지 않거나 변경된 설치는 덮어쓰지 않으며 기존 설치와 선택은 보호합니다. CAD 선택 재준비 실패는 로더 설치와 별도로 표시합니다. Navisworks 로더는 준비 중입니다. 현재 적용 대상은 실행 중 Manager에 동봉된 검증 ZIP이며 GitHub 최신본을 자동 다운로드하지 않습니다. 실제 AutoCAD·Revit 적용 확인은 별도입니다.

## Manager preview.58 · CAD Loader 1.0.2-test.1

이 TEST 버전은 RoBIM-tech/RoBIM-CAD v1.0.2-test.1 ZIP과 SHA-256을 동봉합니다. 프로그램 로더 페이지는 동봉 파일을 적용합니다. AutoCAD 종료와 TEST 권한이 필요하며, 관리 기록과 실제 파일이 다르거나 외부 설치인 경우 덮어쓰지 않습니다. 설치 후 기존 선택 재준비가 실패하면 별도로 알립니다. CAD 실제 UI와 사용자 PC 업데이트·적용은 검증 전입니다.


CAD 리본 편집에서 Reload와 Manager 버튼의 위치·이름·묶음을 편집할 수 있습니다. Manager 자체 업데이트는 Revit/AutoCAD 실행과 독립적이지만 실행 중인 호스트의 고정 로더 교체는 종료 후 적용합니다. 이전 Manager의 업데이트 프로그램은 기존 종료 검사를 유지할 수 있습니다. 실제 호스트 적용은 별도 검증이 필요합니다.


프로그램 로더는 Manager 버전과 별도로 각 GitHub 저장소의 정식 또는 명시적으로 선택한 TEST 릴리스를 조회하고 SHA-256 검증 후 적용합니다. 로더 교체는 호스트 종료와 기존 설치 소유권 확인이 필요합니다. 테스트 채널은 권한이 필요합니다.
