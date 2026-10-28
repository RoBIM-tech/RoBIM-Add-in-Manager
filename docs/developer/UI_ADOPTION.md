# Manager 제거 화면 적용 현황

preview.44 작업본: Windows 제거 확인 후 개인 설정 삭제/유지/취소를 Windows 기본 TaskDialog로 제공한다. 유지가 기본 포커스이며 삭제와 유지 모두 도구·연결·검색 결과·다운로드·백업을 정리한다. 실행 중 프로그램 안내는 재시도/취소 MessageBox다. 취소/닫기는 정리 시작 전 중단하고 실행 상태 무시나 강제 종료를 제공하지 않는다.

기존 Manager 기능 창·리본·공용 UI 라이브러리는 변경하지 않는다. 이는 Windows 설치 제거 화면이며 CAD/Revit 기능 창의 공용 WPF/WinForms 디자인 전환으로 기록하지 않는다. 빌드/엔진 검사는 최종 보고와 UNINSTALL.md를 참조한다. 실제 Windows 화면 조작과 호스트 종료 후 재시도 검증은 미수행이다.


## preview.45 로그인/설정

기존 WinForms 설정 페이지에 자동 로그인·Windows 자동 시작·로그아웃을 추가하고 알림 영역 메뉴를 제공합니다. Windows 정상 환경 가상 계정 UI 검사 4개와 렌더를 통과했습니다. 실제 브라우저 로그인·Windows 재로그인·호스트 실제 리본 변경은 미검증입니다.


## preview.46 로그인 전용 화면

별도 로그인 패널이 브랜드/사이드바/도구 영역을 가립니다. GitHub 로그인·저장 계정 로그인·자동 로그인 선택과 상태 안내를 표시합니다. 성공 시 Revit 도구 홈, 로그아웃 시 로그인 화면으로 전환합니다. 기존 Manager 글꼴/색상/버튼 스타일을 사용합니다. 정상 Windows에서 UI 7개 검사와 렌더 확인을 통과했습니다. Host/CAD Reload의 실제 세션 실행은 미검증입니다.

## preview.48 계정 설정 정리

기본 화면은 현재 계정·자동 로그인·자동 시작과 계정 변경/로그아웃을 제공합니다. 저장 계정 선택과 다른 계정 로그인은 계정 변경 영역, PAT/작업 기록/이전 pyRevit 관리는 고급 설정 영역에서만 표시합니다. NativeOnly에서는 이전 pyRevit 관리가 숨겨집니다. 기존 글꼴·색상·버튼 스타일을 유지하며 새로운 공용 UI 패키지는 추가하지 않았습니다. 정상 Windows에서 UI 6개와 팀 인증 회귀 7개를 통과하고 기본/고급 화면을 확인했습니다.


2026-10-28 실제 PC 적용: 최종 self-contained 실행 파일의 UI 6개와 로그인 잠금 회귀 7개를 재확인하고 Windows Setup으로 preview.47에서 preview.48로 업데이트했습니다. 설치/Native/CAD 상태 JSON과 Revit 리본 파일의 해시 및 Windows 자동 시작 설정이 유지됐습니다. 일반 Windows의 설치 버전·실행 프로세스와 팀 권한 Pipe 응답을 별도로 확인했습니다. GitHub Release 게시/기본 브랜치 반영은 수행하지 않았습니다.


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


## preview.51

Git 없는 기기 인증창, Manager 자동 업데이트 설정/상태, Navisworks 빈 목록을 추가합니다. 기존 Manager 카드/설정 스타일과 Windows 기본 제목 표시줄을 유지합니다. .NET 검사는 버튼 없이 Setup 및 기능 설치 과정에 통합합니다. UI 렌더·인증·실제 설치 검증은 각각 별도 기록합니다.


preview.52: 도구 메뉴와 페이지 제목의 Revit 독립 Add-in을 Revit으로 표시합니다. 내부 모듈·명령·설치 ID와 동작은 유지합니다.


프로그램별 리본 편집: 왼쪽 Revit·AutoCAD·Navisworks 아래에 각각 리본 편집을 둡니다. Revit은 기존 탭/패널/묶음 편집과 자동 적용을, AutoCAD는 기존 CAD 구성의 버튼 이름/패널/순서와 CAD Reload 준비를 사용합니다. CAD 구성을 Revit 리본 파일에 저장하거나 Revit 적용을 호출하지 않습니다. Navisworks는 등록 도구가 없어 편집할 버튼이 없음을 표시합니다. Navisworks 저장·호스트 적용을 지원한다고 표시하지 않습니다.


설정의 Manager 업데이트 확인은 현재/최신 버전 정보만 확인합니다. 새 버전이 있으면 Manager 업데이트 버튼을 표시하며 사용자가 선택하면 검증한 Setup을 준비합니다. 자동 업데이트를 꺼도 수동 확인·설치를 사용할 수 있고, Portable은 배포 페이지를 엽니다.
