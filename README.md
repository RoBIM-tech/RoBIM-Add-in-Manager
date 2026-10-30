# RoBIM Add-in Manager

**새 버튼 제작은 [START_HERE](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/START_HERE.md)에서 시작합니다.**

> 신규 Revit 기능은 독립 Add-in으로 개발합니다. 기존 pyRevit은 유지보수하며, 신규 명령은 자체 검색 메타데이터와 TEST/실사용 구분을 포함해야 합니다. [개발 시작 안내](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NEW_BUTTON.md)

Revit·AutoCAD의 기능 버튼을 설치·제거하고 Reload와 사용자 리본 배치를 관리하는 Windows 앱입니다.

preview.44 작업본부터 Windows 제거에서 관리 도구·연결 모듈·캐시·복구 백업을 함께 정리하고 개인 설정은 삭제/유지를 선택합니다. 실행 중인 프로그램은 재시도/취소 창에서 종료를 기다립니다. 배포·실제 적용 여부는 [제거 검증 기록](docs/developer/UNINSTALL.md)을 확인하십시오.

| 독자 | 시작할 문서 | 설명하는 내용 |
| --- | --- | --- |
| 일반 사용자 | [사용설명서](docs/user/README.md) | 설치·입력·버튼 실행·결과·제거·Reload·문제 해결 |
| 개발자 | [개발자 설명서](docs/developer/README.md) | 구조·진입점·데이터·적용 흐름·실패 복원·빌드·검사 |

[전체 문서 목록](docs/README.md)

## 설치와 개발의 경계

사용자 설치는 RoBIM Add-in Manager에서 진행합니다. 개발자는 설치된 사용자 폴더 대신 이 저장소에서 작업하고 검증된 소스/패키지를 제공합니다. 배포 Release와 현재 검토 작업본의 제공 기능은 다를 수 있습니다.

## 스킬과 연동

전용 스킬 원본은 [`.codex/skills/robim-manager`](.codex/skills/robim-manager/SKILL.md)입니다. 제품 간 계약과 스킬 설치본 동기화는 [RoBIM-Workspace](https://github.com/RoBIM-tech/RoBIM-Workspace)에서 관리합니다. 사용자 리본 표시·설치 선택과 기능 코드의 기본 배치를 구분합니다.


## 버튼별 저장소 운영

신규 Revit 버튼은 **버튼 하나 = Git 저장소 하나 = Native 모듈 하나 = 설치 패키지 하나**로 운영합니다. 다른 버튼은 새 저장소에서 만들며 버튼별 장기 브랜치로 나누지 않습니다. codex/ 브랜치는 해당 저장소 안의 수정 작업용입니다. 공유 구현은 RoBIM-Libraries의 역할별 NuGet으로 제공하고 소비 버전을 정확히 고정합니다. 공용 Host/Loader/Manager를 버튼마다 복제하지 않습니다.

모든 새 버튼은 자체 검색 표시 정보와 Workspace 소유권 목록을 기록하고 TEST부터 시작합니다. README에는 일반 사용자/개발자 설명서와 전용 스킬을 연결합니다. 기존 CAD는 유지하고 pyRevit 저장소는 이력만 보관이며 새 Native 버튼을 추가하는 장소가 아닙니다.

[확정 운영 기준](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_REPOSITORY_REVIEW.md)

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 중앙 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.

## 버튼 제작과 기여

[신규 버튼 제작 순서](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NEW_BUTTON.md)와 [매니페스트·의존성·배포 계약](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_CONTRACT.md)을 따릅니다. 새 버튼은 전용 저장소에서 만들고 Manager 중앙 목록과 Workspace 소유권 맵에 함께 등록합니다. 화면 이름에는 RoBIM 접두어를 반복할 필요가 없습니다. 내부 ID는 이름·버전·저장소 이동 때문에 바꾸지 않습니다.

설치는 카드의 설치하기에서 다운로드·검증·준비로 이어지며 기본 리본 위치를 자동 추가합니다. 기존 사용자 배치는 보존합니다. TEST → 정식은 새 배포와 검증 기록이 필요하고, 소스 병합·Release 게시·PC 설치·호스트 applied 응답은 각각 별도로 확인합니다.

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


신규 버튼 제작은 [Workspace START_HERE](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/START_HERE.md)를 먼저 읽습니다. Preview 37+는 여러 제작자·조직을 등록하고 새로고침에서 새 저장소를 검색합니다. 중앙 등록 PR은 신규 표시의 필수 조건이 아닙니다.


[명명·전달 확인](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_DELIVERY.md) · [패키지/표시 이름 기준](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NAMING.md): 새 제작·기존 수정의 공통 기준입니다. 코드 작성·GitHub 게시·Manager 표시/설치·호스트 적용 상태를 구분합니다.


## pyRevit 제거 후 예전 버튼 잔상

Reload 연결 1.0.0-preview.41은 파일이 제거된 RoBIM pyRevit 버튼이 독립 Add-in과 같은 패널에 남는 경우를 처리합니다. 선택되지 않은 예전 제어는 원래 내부 ID로 식별해 현재 세션에서 비활성·숨김 처리합니다. 파일 제거를 숨김으로 대신하지 않으며 선택된 pyRevit·독립 Add-in·다른 제품의 버튼은 유지합니다. RetiredPythonControls와 BridgeRevision=retirement-1을 적용 기록에 남깁니다. 연결 스크립트만 변경되므로 기존 백업·해시·세션 검증을 통과하면 Revit 실행 중 연결 업데이트와 표준 pyRevit Reload를 사용합니다. 실제 적용 완료는 새 세션 일치 응답과 기록을 확인합니다.


## RoBIM pyRevit 사용 종료

RoBIM은 독립 Native 버튼으로 운영합니다. 기존 기능과 Python 리본 연결은 [Native 전환 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/PYREVIT_RETIREMENT.md)에 따라 백업·검증 후 제거합니다. NativeOnly에서는 Host가 리본/Reload를 담당하며 pyRevit 본체·다른 확장은 유지합니다. 고정 Host 교체는 Revit 종료가 필요합니다. 새 PC 적용·재시작 후 검증 전에는 완료로 기록하지 않습니다.


## NativeOnly 연결 호환성 — Manager 1.0.0-preview.43

동봉 Native Host는 1.0.0-preview.4입니다. preview.3에서 발견한 UI JSON 키 대소문자 문제를 수정한 Host부터 RoBIM pyRevit 제거의 필수 조건을 충족합니다. 기존 Manager UI 연결 계약은 유지합니다. 종료 후 정상 Setup 설치와 재시작 세션의 실제 applied 응답을 별도로 검증합니다. 이전 TEST 배포 파일은 교체하지 않습니다.


2026-10-27 실제 PC 적용: 정상 Windows Setup으로 Manager 1.0.0-preview.43 및 Host 1.0.0-preview.4를 설치했습니다. Revit 재시작 후 Native Provider, 독립 기능 8개·관리 버튼 2개를 확인했습니다. Manager.Core의 RequestRevit(false)가 생성한 요청 ID와 Host의 applied 응답이 일치했고, 응답 시각이 안정적으로 유지되어 반복 적용이 없음을 확인했습니다. RoBIM Python 확장 0개·다른 확장 9개 및 기존 Native 선택을 보존했습니다. 모델을 열거나 모델 명령을 실행하는 검증은 수행하지 않았으며 TEST 배포입니다. 기본 브랜치 병합은 수행하지 않았습니다.


## GitHub 팀 인증 (preview.45 작업본)

RoBIM-tech / robim 팀의 활성 구성원만 사용할 수 있습니다. 자동 로그인·Windows 로그인 시 알림 영역 자동 시작과 로그아웃 후 도구 잠금을 제공합니다. Host/Loader 업데이트와 재시작이 필요합니다. [구조·검증 범위](docs/developer/TEAM_ACCESS.md)


## 로그인 전용 첫 화면 — preview.46

로그인 전에는 메뉴·도구 목록을 숨기는 전용 화면을 표시합니다. 인증 후 도구 홈으로 이동하며 로그아웃하면 첫 화면으로 돌아갑니다. Reload와 Manager 열기는 로그인 없이 허용하고 실제 기능 실행은 계속 팀 인증을 확인합니다. 동봉 Host preview.6 / CAD Loader 1.0.1-test.2는 재시작 후 적용됩니다.


## RoBIM-tech 조직 이전

2026-10-27 RoBIM 전체 15개 저장소 이전과 현재 PC의 Manager preview.47 연결 검증을 완료했습니다. 기존 설치 ID를 유지합니다. [이전 현황·보존 기록·남은 조직 앱 승인](docs/developer/ORGANIZATION_TRANSFER.md)

preview.48은 [계정 설정](docs/user/README.md#계정-설정-preview48)의 중복 로그인·연결 작업을 정리합니다. 계정 변경과 고급 설정은 필요할 때 펼칩니다.

[preview.48 TEST 배포와 검증 기록](docs/developer/PREVIEW48_RELEASE.md): 소스 PR·설치 파일·공용 인증/Host/CAD 패키지와 남은 검증 범위.


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


Manager 1.0.1 후보는 Manager 자체 업데이트에서 Revit/AutoCAD 종료를 요구하지 않습니다. 기존 버전에서 처음 전환할 때는 기존 검사에 따라 종료가 필요할 수 있습니다. 고정 로더 교체에는 해당 호스트 종료가 필요합니다.


## CAD 독립 버튼 TEST 전환 · 2026-10-29

현재 분리 작업은 [전환 계약](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/codex/cad-loader-button-split/docs/developer/CAD_INDEPENDENT_PACKAGES.md)을 따릅니다. CAD 로더 저장소는 RoBIM-CAD-Loader로 변경됐으며 기능 4개는 별도 저장소입니다. 후보 조회는 제외합니다. 기존 설치·명령 ID와 정식 Release는 보존합니다. 새로운 패키지는 TEST이며 실제 호스트 검증과 main 병합·정식 승격은 별도입니다. 이전 통합 CAD 공급 설명보다 이 전환 계약이 우선합니다.


## Loader TEST 1.1.0-test.3

로더 ZIP 이름은 RoBIM-CAD-Loader-AutoCAD2026.zip입니다. 기존 설치 폴더·ID는 유지하며 Manager는 구 이름의 정식 Release도 읽습니다. 업데이트 비활성 이유는 로컬 변경/외부 설치 보호, 저장소 확인 필요, 제공 버전 없음, 최신/이전/비교 불가로 구분합니다. TEST를 선택한 경우 설치 1.0.2-test.2 → 제공 1.1.0-test.2는 업데이트 대상이며 파일 보호 조건은 별도로 적용합니다. [변경 기록](docs/developer/LOADER_ARCHIVE_AND_ACTIONS_20261030.md).

사용자 추가 승인에 따라 설치 폴더도 RoBIM.Cad.Loader.bundle로 변경합니다. 구 DelHiddenNet.bundle은 정상 관리 설치만 호스트 종료·해시 확인·백업 후 새 경로로 이전하고, 저장 실패 시 구 경로와 기록을 복구합니다. 실제 설치 ID와 명령 ID, 리본 파일은 유지합니다. 로컬 변경·외부 설치·신구 경로 충돌은 차단합니다.


2026-10-30 TEST 4 전환: docs/developer/MANAGER_WORKSPACE_TEST4.md의 로더 식별자·프로그램별 상태·리본 배치 계약을 적용한다.
