# Manager 제거 — preview.44

Windows 제거 확인 이후 usUninstall에서 로컬 Manager의 --uninstall-user-data를 실행한다. 확인/설정 선택에서 취소하면 사용자 데이터를 변경하지 않는다. 정리 명령이 실패하면 Abort하여 Manager 실행 파일을 남긴다. InitializeUninstall은 경로·소유권·Manager 종료를 확인하며 실패 시 재시도/취소를 제공한다. 정리 명령도 Revit/AutoCAD 종료를 재확인하고 동일한 재시도/취소 동작을 사용한다.

개인 설정 선택은 TaskDialog의 삭제/유지/취소다. 유지 시 github-auth.json, repository-publishers.json, pyrevit-location.json, ribbon-layout-revit.json, ribbon-layout-autocad.json, revit-mode.json과 Manager 전용 Windows 인증을 보존한다. 설치 기록·설치 선택·수동 등록·검색 캐시·다운로드·Native/CAD 적용 요청·백업은 두 모드 모두 정리한다. 공용 Git Credential Manager 인증은 건드리지 않는다.

ManagerUninstall은 전체 경로의 링크, 설치 기록과 허용된 부모 폴더, 해시, .git 여부, Host 등록 파일의 assembly/클래스/ID 및 pyRevit 설정을 사전 검사한다. 도구 제거는 InstallService.Remove의 기존 백업/상태 갱신을 재사용한다. 사용자 변경·외부 Host 등록·실행 중 호스트 때문에 실패하면 명확한 안내와 함께 중단하며 외부 도구를 채택하거나 삭제하지 않는다. 설정 삭제 시 RoBIM.Manager/GitHub/* 자격 증명만 열거·제거한다.

Host 등록은 사용자 Addins/<연도>/RoBIM.NativeHost.addin 중 Manager의 Host DLL 경로·고정 ID·클래스가 일치하는 경우만 제거한다. pyRevit 공유 INI에서는 설치 기록에 있는 확장 이름의 섹션과 해당 확장 자체를 가리키는 검색 경로만 제거한다. 공유 검색 상위 폴더와 다른 섹션은 보존한다. 전체 정리는 현재 Windows 사용자 범위이며 다른 사용자나 수동 설치를 포괄하지 않는다.

무인 제거는 실행 중 호스트나 오류가 있으면 UI 없이 실패한다. 기본은 개인 설정 삭제이며 Inno /KEEPSETTINGS=1 또는 Portable -KeepSettings는 유지한다. Portable -KeepUserData는 개발/설치 격리 시험용 실행 파일만 제거하는 호환 옵션이다. TestBuild Setup은 사용자 데이터 정리를 생략하여 실제 설치를 보호한다.

## 검증 상태

2026-10-27: 전체 엔진 212/212(제거 전용 8개 포함), 리본 활성화·관리 배치·중앙 목록·퇴역 리본 회귀 검사를 통과했다. preview.44 self-contained 빌드와 정식/TestBuild Inno 컴파일을 통과했다. NuGet 취약성 서비스 접근 실패 NU1900 경고가 있어 온라인 취약성 조회는 미검증이다.

설치 실행 검증은 미완료다. Portable 임시 설치/재설치는 실행됐으나 화면 렌더가 15초 제한을 초과하여 제거 단계로 진행하지 못했다. Native 설치 검사는 실행 환경에서 존재하지 않는 임의 GUID HKCU 키도 Test-Path=True로 반환하는 것을 확인했고, 기존 테스트 등록 보호 조건에서 중단됐다. 기존 사용자 등록을 삭제하거나 보호 조건을 우회하지 않았다. 따라서 앱 등록/설치 제거 및 새 선택/재시도 창의 실제 UI 동작은 성공으로 기록하지 않는다.

임시 경로의 자동 검사와 빌드 결과는 최종 작업 보고에 기록한다. 새 코드의 실제 사용자 전체 제거, 실행 중 호스트에서 재시도/취소 창 조작, 개인 설정 선택 창 조작, GitHub TEST Release 게시 및 일반 Windows PC 적용은 아직 미검증/미수행이다. 과거 설치 검증을 이번 변경의 실제 제거 검증으로 대신하지 않는다. 공통 기능 창/버튼 리본 디자인은 변경하지 않았으며 설치 제거용 Windows 기본 TaskDialog를 사용한다.


preview.45 재검증: 정상 Windows 환경에서 Portable 임시 설치/재설치/제거와 Native Setup의 고유 시험용 앱 등록/업그레이드/제거/외부 파일 보존 검사를 통과했습니다. 앞선 제한 환경 실패를 해결했으며 실제 사용자 전체 제거·개인 설정 선택/재시도 창 조작은 여전히 미수행입니다.
