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
