# RoBIM Add-in Manager 기술 구조

> 초기 구현·설계 이력을 포함한 기술 참고입니다. 현재 사용자 조작은 [사용설명서](user/README.md), 현재 적용 경로는 [개발자 설명서](developer/README.md)를 우선합니다.


## Preview 7 CAD 연결

기존 Windows Manager의 AutoCAD 화면에서 연결 모듈 최초 설치와 기능 버튼 선택/적용 준비를 구분한다. 연결 모듈 설치·업데이트/제거/복원은 호스트 종료가 필요하다. 이후 호환 기능과 버튼 정의는 별도 `CadReload` 경로로 준비하며 일반 설치 보호를 해제하지 않는다.

`Manager.Core/CadConfiguration.cs`는 CAD와 독립 구현한 JSON 계약이며 Autodesk 참조가 없다. `CadReload`는 설치 해시·코어 DLL 해시·상대 경로·선택 ID를 확인하고 `%LOCALAPPDATA%/RoBIM/Manager/Cad/Snapshots/<ID>`에 완성된 payload를 준비한다. CAD는 명령 컨텍스트의 ROBIMRELOAD에서 PID/시작 시각과 파일 해시를 다시 검증하고 active.json/Results에 적용 상태를 기록한다. 타깃 PID 0은 CAD가 종료된 상태에서 다음 실행에 적용하도록 준비한 경우다.

pending.json이 있다는 사실은 적용 완료가 아니다. Manager는 Results 응답을 읽어 준비됨/적용 완료/실패를 표시한다. CAD 로더가 바뀐 준비 요청은 다시 준비하도록 안내한다. 사용자별 구성은 설치 파일 밖에 있어 일반 설치 기록과 로컬 변경 검사를 보존한다.

이 기능은 RoBIM 호환 catalog에 한한다. 타사 DLL을 일반적으로 언로드하는 도구가 아니며 Revit의 기존 binary 변경 차단 규칙은 유지한다. Manager EXE와 CAD 고정 코어 변경은 재시작 대상이다.

사용 순서는 [간단 사용설명서](USER_GUIDE_KO.md)를 참고하십시오.

이 저장소는 매니저·Setup·Reload 연결과 문서를 관리합니다. CAD는 RoBIM-tech/RoBIM-CAD, Revit은 RoBIM-tech/RoBIM-pyrevit에서 관리합니다. 초기 구현의 이전 Git 이력은 CAD 저장소의 과거 브랜치에 보존하고 현재 작업과 배포는 이 저장소로 분리합니다.

- Manager.Core: .NET 10 설치·백업·복원·GitHub 인증·패키지 검색·설치 흐름 정책
- RoBIM.Manager: Windows Forms. Revit / AutoCAD 제품별 목록과 한 개의 기본 작업 버튼
- installer/Manager.iss: Inno Setup 기반 사용자별 Windows 설치·업데이트·전용 제거 프로그램. 기존 사용자 설치와 설정 유지
- reload-bridge: pyRevit 원본을 수정하지 않는 별도 연결 확장
- tests: 임시 경로 설치 및 실제 GitHub 읽기 검증

일반 설치·제거·복원은 호스트 종료를 요구합니다. 기존 Revit Git 사본은 로컬 변경을 보존하고 현재 추적 브랜치의 fast-forward만 허용합니다. 실행 중 Revit은 준비만 한 뒤 단일 세션 PID·시작 시각, 준비 해시, 기존 상태를 재검사하는 내부 버튼으로 적용합니다. DLL/EXE/CS 변경은 재시작 대상으로 차단합니다.

GitHub 브라우저 계정은 GCM, PAT는 RoBIM 전용 Windows 자격 증명에 저장합니다. 설정·로그·명령줄·Git 설정·pyRevit INI에 토큰을 기록하지 않습니다. 등록된 임의 저장소의 빌드/설치 스크립트는 실행하지 않습니다.

기존 설치와의 호환성을 위해 내부 실행 파일명 RoBIM.Manager.exe, 기존 Programs/RoBIM Manager 경로, 설정 디렉터리, 뮤텍스·이벤트와 레지스트리 키는 유지합니다. 화면 이름·바로가기·Windows 앱 이름은 RoBIM Add-in Manager입니다.

빌드는 build-manager.ps1을 사용합니다. CAD ZIP/SHA-256이 없으면 fetch-cad-package.ps1이 CAD Releases에서 검증 후 받습니다. CAD 소스 및 Autodesk SDK는 이 저장소 빌드에 필요하지 않습니다.

관리 데이터는 %LOCALAPPDATA%/RoBIM/Manager입니다. installed.json과 Backups/ExternalBackups는 설치 상태·복구 사본, Downloads/Prepared/ReloadPackages는 캐시, github-auth.json은 방식·ID만, reload-pending.json/reload-profile.json은 대기 요청을 기록합니다. 실행 사본의 변경이 감지되면 덮어쓰지 않습니다.

패키지 규격: [EXTENSION_PACKAGES.md](EXTENSION_PACKAGES.md). 검증: [MANAGER_VALIDATION.md](MANAGER_VALIDATION.md).
