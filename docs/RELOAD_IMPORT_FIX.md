# pyRevit Reload import 수정

2026-10-20 로컬 Preview24 검증 작업본입니다. GitHub Release 게시 버전과 구분합니다.

- Revit의 IronPython에서 `Cannot import name FileVersionInfo`가 발생했습니다. `System.Diagnostics.Process`만 명시적으로 로드한 스크립트가 별도 어셈블리의 타입을 가져오려 했습니다.
- 연결 스크립트에 `System.Diagnostics.FileVersionInfo` 참조를 추가했습니다. 모델·DLL·리본 선택은 변경하지 않았습니다.
- 설치된 pyRevit IPY2712PR 엔진/.NET 8.0.28에서 기존 import 오류를 재현했습니다. 수정된 실제 소스의 CLR import, 프로세스 접근, Preview24 EXE의 ProductVersion 조회가 통과했습니다.
- Manager 관리 설치 스크립트·파일 해시 기록·연결 ZIP을 동기화했고, 사용자가 실행 중인 Revit에서 오류 없이 Reload되는 것을 확인했습니다.
- Portable과 Setup을 수정된 연결 ZIP으로 재생성했습니다. 임시 설치·재설치·UI 렌더·제거·알 수 없는 파일 보존·외부 경로 보호 및 동봉 패키지 해시 검사가 통과했습니다.
- Manager 회귀 검사 139/139가 통과했습니다. 이 변경으로 새 기능의 모델 수정 동작까지 검증했다고 주장하지 않습니다.
