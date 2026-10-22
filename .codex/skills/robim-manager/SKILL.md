---
name: robim-manager
description: RoBIM Add-in Manager의 버튼 카드·선택 설치·설치 위치·리본 편집·버전/패키지·Reload·Windows 배포를 개발하거나 진단한다. 기능 알고리즘은 각 도구 저장소에서 수정한다.
---

# RoBIM Add-in Manager

이 저장소는 Manager.Core, RoBIM.Manager, 설치 프로그램과 Manager 소유 pyRevit 연결을 소유한다. README.md, docs/USER_GUIDE_KO.md, AGENTS.md 및 변경 영역의 구조 문서를 읽는다. Windows x64 / .NET 10이다.

## 기능과 설치 정책

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
- 제작자와 관계없이 새 명령은 Manager catalog/modules.json에 등록한다. 기능 PR과 등록 PR을 연결하고 scripts/validate_registration.py로 배포 전 검사한다. 저장소 생성만으로 다른 PC에 자동 등록된다고 설명하지 않는다.
- 새 배포는 TEST이며 Manager TEST 도구에 표시한다. 실사용 승격은 새 정식 버전과 실제 호스트 검증자·빌드·HTTPS 기록이 필요하다. 버전명·브랜치·설치 성공만으로 승격하지 않는다.
- 신규 제작 시 통합 저장소 docs/developer/NEW_BUTTON.md와 scripts/new_revit_module.py를 읽는다. 생성 도구는 Manager clone을 필수로 받아 등록 초안을 함께 만든다.


## 버튼별 저장소 운영

신규 Revit 버튼은 **버튼 하나 = Git 저장소 하나 = Native 모듈 하나 = 설치 패키지 하나**로 운영합니다. 다른 버튼은 새 저장소에서 만들며 버튼별 장기 브랜치로 나누지 않습니다. codex/ 브랜치는 해당 저장소 안의 수정 작업용입니다. 공유 구현은 RoBIM-Libraries의 역할별 NuGet으로 제공하고 소비 버전을 정확히 고정합니다. 공용 Host/Loader/Manager를 버튼마다 복제하지 않습니다.

모든 새 버튼은 Manager 중앙 목록과 Workspace 소유권 목록에 함께 등록하고 TEST부터 시작합니다. README에는 일반 사용자/개발자 설명서와 전용 스킬을 연결합니다. 기존 CAD 묶음·기존 pyRevit은 호환성을 유지하는 레거시이며 새 Native 버튼을 추가하는 장소가 아닙니다.

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 중앙 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.

## 버튼 제작 기여 확인

새 버튼 또는 배포/등록 변경 시 [버튼 제작 계약](https://github.com/gagaggan/RoBIM-Workspace/blob/main/docs/developer/BUTTON_CONTRACT.md)을 읽는다. 표시 이름과 안정된 ID, 패키지와 중앙 등록의 일치, 고정 NuGet 버전별 공급원, 일반 사용자/개발자 설명서, TEST Release와 정식 승격 후 PC 적용을 함께 확인한다. 병합을 새 Release 게시나 사용자 설치 허가로 해석하지 않는다. 기존 기능 유지보수 저장소에 새 버튼을 넣지 않는다.

## 새로고침과 업데이트 표시

카드의 새로 고침은 로컬 설치 상태와 원격 기본 브랜치의 배포 정보·채널별 최신 버전을 확인합니다. 주황은 업데이트 가능, 연녹색은 설치/선택됨, 회색은 미설치이며 상태 문구도 함께 표시합니다. 업데이트가 있으면 버튼이 업데이트로 바뀌고 설치 버전 → 제공 버전을 표시합니다. 업데이트 먼저 정렬도 제공합니다. 업데이트를 누르면 검증된 패키지를 준비하고 실행 중인 Revit에 자동 적용을 요청합니다. CAD는 AutoCAD Reload를 사용합니다. 새로고침 자체는 패키지 다운로드·설치·모델 변경을 하지 않습니다.

시작·탭 전환·로컬 타이머는 네트워크를 조회하지 않습니다. 저장소 조회 실패 시 기존 설치와 마지막 확인 결과를 유지하고 최신 확인 실패를 표시합니다. 비교할 수 없는 외부/동봉 버전은 임의로 업데이트라고 판정하지 않습니다. Native는 실제 선택 모듈 버전, 기존 pyRevit은 선택 스냅샷의 브랜치·commit, CAD는 기록된 배포 버전을 비교합니다. 정식 설치에 TEST를 자동 제공하거나 새 버전을 자동 설치하지 않습니다.

Manager의 Revit 설치·업데이트·제거 및 리본 편집의 Revit 적용은 별도 Reload 클릭 없이 자동 적용을 요청합니다. Native Host는 세션 ID를 확인해 Idling에서 처리하고, 기존 pyRevit은 연결 확장의 ExternalEvent에서 적용·Reload합니다. 실행 중인 명령·모달 창이 끝나면 처리하며, 최초 Host 설치나 고정 로더/pyRevit 바이너리 교체는 기존 재시작 조건을 유지합니다. 요청 전송과 실제 applied 응답을 구분하고 다른 버튼의 캐시 업데이트는 자동으로 끌어오지 않습니다. 수동 Reload는 재시도용입니다.
