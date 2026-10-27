# GitHub 팀 인증 — Manager preview.46

정책은 GitHub RoBIM-tech 조직의 robim 팀이다. 사용자 입력으로 허용 팀을 바꾸지 않는다. 저장된 GitHub 인증으로 /user에서 계정을 식별하고 /orgs/RoBIM-tech/teams/robim/memberships/{login}의 state=active일 때만 허용한다. 초대 대기·404·403·잘못된 응답·통신 실패는 거부한다. 로그인 성공과 팀 권한 확인 성공을 구분한다. 현재 GCM 저장 인증으로 HTTP 200/active를 확인했으며 토큰은 출력하거나 파일로 복사하지 않았다.

첫 실행은 로그인 전용 화면이다. 로그인 전에는 메뉴와 도구 목록을 숨기며 GitHub 로그인/저장 계정 로그인과 자동 로그인 선택만 제공한다. 인증 후 Revit 도구 화면으로 이동하고 로그아웃/인증 실패 시 로그인 전용 화면으로 돌아간다. 자동 로그인(기본 켜짐)은 기존 Windows 자격 증명을 조용히 재사용하며 브라우저를 자동으로 열지 않는다. 수동 GitHub 로그인은 기존 Git Credential Manager 브라우저 인증을 사용한다. Git/GCM 사전 설치가 필요하며 조직의 OAuth 접근 승인이 제한되어 있으면 관리자 승인이 필요하다. 별도 RoBIM 비밀번호는 만들거나 저장하지 않는다. 인증 만료/철회 시 재로그인이 필요할 수 있다.

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
