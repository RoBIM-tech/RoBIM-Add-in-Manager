# GitHub 팀별 권한


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


## 이전 버전 검증 기록

아래 내용은 각 버전 당시의 기록이며 현재 권한·버전은 위 정책을 적용합니다.

# GitHub 팀 인증 — Manager preview.46

정책은 GitHub RoBIM-tech 조직의 robim 팀이다. 사용자 입력으로 허용 팀을 바꾸지 않는다. 저장된 GitHub 인증으로 /user에서 계정을 식별하고 /orgs/RoBIM-tech/teams/robim/memberships/{login}의 state=active일 때만 허용한다. 초대 대기·404·403·잘못된 응답·통신 실패는 거부한다. 로그인 성공과 팀 권한 확인 성공을 구분한다. 현재 GCM 저장 인증으로 HTTP 200/active를 확인했으며 토큰은 출력하거나 파일로 복사하지 않았다.

첫 실행은 로그인 전용 화면이다. 로그인 전에는 메뉴와 도구 목록을 숨기며 GitHub 로그인/저장 계정 로그인과 자동 로그인 선택만 제공한다. 인증 후 Revit 도구 화면으로 이동하고 로그아웃/인증 실패 시 로그인 전용 화면으로 돌아간다. 자동 로그인(기본 켜짐)은 기존 Windows 자격 증명을 조용히 재사용하며 브라우저를 자동으로 열지 않는다. preview.51 수동 GitHub 로그인은 RoBIM 조직 소유 OAuth 앱의 기기 인증을 직접 HTTPS로 수행한다. Git/GCM 설치가 필요하지 않다. 조직의 OAuth 접근 승인이 제한되어 있으면 관리자 승인이 필요하다. 별도 RoBIM 비밀번호는 만들거나 저장하지 않는다. 인증 만료/철회 시 재로그인이 필요할 수 있다.

팀 권한은 시작 및 2분 간격으로 재확인하며 마지막 성공은 최대 3분 동안만 유효하다. 검증 실패는 즉시 잠근다. 로그아웃은 Manager의 연결 선택만 해제하고 공용 Git 자격 증명은 지우지 않는다. 진행 중 조회가 로그아웃을 취소하지 않도록 세대 번호를 검사한다. 자동 로그인 끄기는 다음 시작에 적용한다. 네트워크 불통에도 실행을 허용하는 오프라인 권한은 제공하지 않는다.

RoBIM.Access.Core [0.1.0-test.3] 소스 NuGet을 Manager.Core·Native Host·CAD Loader가 각 어셈블리에 컴파일한다. Manager만 GitHub 인증을 가지며 동일 Windows 사용자만 접근하는 Named Pipe로 현재 허용 여부 한 바이트를 전달한다. 호스트는 1초 간격으로 조회하고 3초가 지난 로컬 상태를 허용하지 않는다. 실제 명령 실행 직전에도 새 연결로 확인한다. 계정 토큰과 편집 가능한 허용 파일을 호스트에 전달하지 않는다. 사용자 기기의 변조된 실행 파일까지 차단하는 서버 라이선스 시스템은 아니다.

Manager 창 닫기는 알림 영역으로 숨기며 인증을 유지한다. 알림 영역에서 열기·로그인·로그아웃·종료를 제공한다. 종료하면 권한 제공도 끝난다. Manager 설치 시 선택된 startup 작업은 현재 사용자 HKCU Run에 따옴표로 감싼 실행 파일과 --background를 등록한다. 설정에서 자동 시작을 변경할 수 있으며 제거 시 자신의 시작 항목을 정리한다. Windows 전체 부팅 시점이 아닌 해당 사용자의 Windows 로그인 시점에 실행한다.

잠긴 상태에서는 Manager 제품/설치/리본 영역과 일반 작업 실행을 막는다. 로그인/설정/종료는 가능하다. Revit Host preview.6와 CAD Loader 1.0.1-test.2는 기능 리본/명령 dispatch의 팀 인증을 확인한다. 로그인하지 않아도 Reload와 로그인 진입용 Manager 열기는 허용한다. 예외는 정확한 Reload/Manager 버튼 ID에 한정하며 다른 관리 명령과 실제 기능 실행에는 확장하지 않는다. CAD의 ROBIMRELOAD/DHNETRELOAD/EZSPLITRELOAD만 전용 Reload 경로를 사용하고 실제 기능 Guard와 CadRuntime.Execute는 계속 인증한다. 기존 ID와 사용자 리본 배치는 유지한다. 구형 Loader/Host와 퇴역 Python·수동 직접 로드 기능에 소급 적용되지 않으므로 고정 Host/Loader 업데이트와 Revit/AutoCAD 재시작이 필요하다.

개인 설정 유지에는 account-preferences.json(자동 로그인)을 추가한다. 제거해도 자동 시작 등록은 남기지 않는다. 계정 설정 유지/삭제와 GitHub 전체 로그인 해제는 별개다.

## 공급과 검증

Libraries artifacts/packages에서 고정 소스 패키지를 공급한다. Manager build-manager.ps1 -LibraryFeed, Native build-host.ps1 -LibraryFeed, CAD build.ps1 -CommonFeed에 해당 폴더를 전달한다. 다른 개발자가 받을 수 있는 GitHub TEST 배포는 게시 결과를 별도로 기록한다. 기존 같은 버전 패키지를 교체하지 않는다.

팀 결정 자동 검사 7개와 정상 Windows 환경에서 Named Pipe 검사 8개를 통과했다. Manager UI 가상 계정 검사 4개에서 로그인 전 잠금·작업 미실행·인증 후 활성화·로그아웃 후 잠금을 확인했고 화면을 렌더했다. 제한된 실행 환경에서는 Named Pipe가 거부되어 정상 Windows 실행으로 재검증했다. 실제 계정의 신규 브라우저 로그인·Windows 재로그인 자동 시작·로그아웃 후 Revit/CAD 실제 명령/리본 변화는 별도 호스트 검증 대상이며 아직 완료로 기록하지 않는다.


2026-10-27 추가 검증: Manager 엔진 219/219, 정상 Windows UI 4개와 공용 Pipe 8개, Native Reload 11개 검사를 통과했습니다. 새 Windows Setup·Portable의 임시 설치/재설치/제거·외부 파일 보존 검사도 정상 Windows 환경에서 통과했습니다. 현재 사용자 Revit/AutoCAD에서 실제 모델과 도면이 열려 있어 고정 Host/Loader 적용은 사용자 저장·종료 응답을 기다립니다. 기존 제품 설치를 강제 변경하지 않았습니다. Workspace 검사는 NativeProtocol/CadConfiguration 바이트 일치를 확인했으나 Workspace 체크아웃의 Git 루트 접근 오류로 전체 검사는 통과하지 못했습니다.

정상 Windows 환경 재확인: Workspace 24개 검사가 모두 통과했고 NativeProtocol/CadConfiguration 계약도 일치합니다. 이번에 수정한 Manager/Host/CAD/Libraries 전용 스킬 4개는 통합 install_skills.py로 기존 설치본을 백업 후 동기화했습니다. 다른 스킬은 변경하지 않았습니다. 최종 배포 실행 파일로 가상 계정 UI 4개 검사를 다시 통과했으며 Manager preview.45 + Host preview.5 + CAD Loader 1.0.1-test.1과 Access.Core 0.1.0-test.3을 묶은 로컬 TEST 설치 파일을 준비했습니다. GitHub 게시/기본 브랜치 병합/실제 사용자 Host 적용은 미수행입니다.


preview.46 검증: 로그인 전용 화면·메뉴 숨김·인증 후 도구 홈 진입·로그아웃 복귀 등 정상 Windows UI 7개 검사를 통과했습니다. Manager46/Host6/CAD Loader test.2 SDK 빌드가 통과했습니다. 실제 Revit/CAD의 로그인 전 Reload와 로그인 후 모델 명령 실행은 아직 미검증이며 기존 설치는 변경하지 않았습니다. 이전 preview.45의 Reload 잠금 설명은 이 정책으로 대체합니다.

최종 preview.46 패키지 검증: self-contained 실행 파일의 UI 7개 검사와 로그인 화면 렌더, Portable 임시 설치/재설치/제거 및 Native Setup 고유 시험용 설치/업그레이드/제거 검사를 통과했습니다. Workspace 24개 검사 통과, Manager/Host/CAD 스킬 원본 3개를 백업 후 동기화했습니다. 최종 로컬 Setup: RoBIM-Manager-Setup-1.0.0-preview.46-TEST.exe. 실제 사용자 설치·기본 브랜치 반영·GitHub TEST 게시·호스트 세션의 Reload 실행은 미수행입니다.

## 2026-10-28 현재 PC의 고정 Host/Loader 설치

사용자가 Revit과 AutoCAD를 종료한 뒤 두 프로세스가 없음을 재확인하고 일반 Windows 사용자 환경에서 Manager의 InstallService/NativeRevitCommands로 설치했다. 실제 LocalAppData 경로 검사를 통과했으며 동봉 ZIP/SHA-256을 검사했다.

- Revit Host: 1.0.0-preview.4 → 1.0.0-preview.6.
- CAD Loader: 이전 동봉 버전 → 1.0.1-test.2. CAD 기능 패키지도 같은 검증된 동봉 패키지로 설치했다.
- 기존 설치는 Manager Backups에 복구 사본을 남겼다. 설치 후 파일 전체가 준비된 패키지와 일치하고 관리 상태의 Modified=false를 확인했다.
- Native active/pending JSON은 해시가 유지됐다. CAD의 기존 버튼 4개(delhidden:cleanup, ezsplit:split, exbx:explode, xclip:cut)를 새 Loader용 pending 요청에 그대로 보존했다.
- 모델/DWG를 열거나 변경하지 않았다. 재실행한 실제 Revit/CAD 세션의 로그인 전 Reload, 로그인 전후 버튼 잠금 및 모델 명령 실행은 아직 검증하지 않았다. 다음 실행부터 새 고정 Host/Loader가 로드된다.
- 로컬 검증 기록: artifacts/host-update-20261028/result.json. GitHub Release 게시와 기본 브랜치 반영은 수행하지 않았다.


## preview.51 · Git 없는 로그인과 .NET 확인

GitHub로 로그인을 누르면 인증 코드와 GitHub 인증 페이지가 표시됩니다. 코드 입력·앱 승인 후 연결 계정을 직접 확인하고 Windows 자격 증명 관리자에 Manager 전용 인증을 저장합니다. Git/GCM 설치는 필요하지 않습니다. 기존 Git/GCM 연결 계정은 한 번 다시 로그인해야 합니다. 만료·철회된 인증은 재로그인하며 자동 시작은 브라우저를 열지 않습니다. 계정 목록도 Manager 전용 저장소에서 읽고 공용 GitHub 인증은 변경하지 않습니다. 이전 Git 작업 사본을 관리하는 고급 레거시 기능은 여전히 Git을 필요로 합니다.

Setup은 설치 전에 .NET 실행 환경을 자동 검사합니다. 별도 확인 버튼을 두지 않습니다. Manager는 .NET을 포함해 배포하므로 별도 .NET 설치가 필요 없습니다. Revit 2026.5+/AutoCAD 2026.1.2+의 runtimeconfig와 x64 .NET/Windows Desktop/호스트가 요구하는 ASP.NET Core 실행 환경을 검사합니다. 다른 .NET 버전이나 x86 설치만으로 충족 처리하지 않습니다. 기능 설치 전에도 해당 호스트의 실행 환경을 확인합니다. 등록된 설치 경로와 C:\Revit 2026 같은 별도 경로를 지원하고 찾지 못하면 설치 폴더를 지정합니다. 호스트가 .NET 8인 경우와 .NET 10 x64 런타임 누락을 구분하여 안내합니다. SDK는 사용자 설치 요건이 아닙니다.

정식 전환 대상은 시트 생성·파라미터 복사·중앙파일 복제/워크셋 수정·링크 경로 변경·뷰 복제 및 CAD 전체입니다. 사용자 gagaggan은 최신 TEST 실사용을 Revit 26.5.0.55/AutoCAD R25.1.179.0에서 검증했다고 확인했습니다. 배포/Manager 새로고침/설치/실제 호스트 적용은 각각 확인합니다.


preview.51은 Setup 설치본의 Manager 자체 자동 업데이트를 제공합니다. 로그인 후 6시간 간격으로 Manager Release만 확인하고 새 버전의 고정 Setup asset을 GitHub SHA-256 digest로 검사합니다. 정식 Manager는 TEST로 자동 전환하지 않으며 현재 preview 계열은 후속 preview와 정식 승격을 받을 수 있습니다. 설정에서 자동 업데이트를 끌 수 있습니다. 도구 선택/기능 버전은 자동 변경하지 않습니다. 작업 중이거나 Revit·AutoCAD가 실행 중이면 적용을 대기하며 종료 후 설치 프로그램으로 교체·재시작합니다. Portable은 자동 덮어쓰지 않습니다. 설치 로그와 업데이트 결과는 Manager 데이터의 ManagerUpdates 하위에 기록합니다.

Navisworks 메뉴는 등록 도구가 없는 빈 목록과 기본 경로 설치 버전을 제공합니다. Navisworks 플러그인 패키지 설치·실행 지원을 완료한 것은 아닙니다. .NET 8 호스트 지원은 이번 범위에서 제외합니다.
