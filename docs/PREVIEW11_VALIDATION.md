# Preview 11 · 실행 중 pyRevit 버튼 선택 · 2026-10-20

기존 버튼 선택 설치는 일반 설치의 Revit 종료 검사를 재사용했으나, 이제 실행 중에는 준비하고 Revit 안의 기존 연결 버튼에서 적용 후 표준 pyRevit Reload를 호출한다. pyRevit 프로그램 원본과 운영 Git 사본은 변경하지 않는다.

- Manager 엔진 102/102: 연속 선택 병합과 준비 중 무변경, 최초 동일 바이너리 복사와 후속 DLL 변경 거부, 활성화 실패 시 최초/기존 설치 복원 및 재시도, 잘못된 세션/원본 변경/준비 파일 변조 거부, 다른 업데이트·프로필 요청과 충돌 방지 포함.
- 실제 배포 WinForms 예시 UI 61/61. 카드/검색/설치·제거/레이아웃 검사이며 실제 Revit 안의 pyRevit Reload를 클릭한 결과를 뜻하지 않는다.
- 독립 Native Host 실 Revit 검증: PID 44196, 시작 ticks 639279967238764271를 유지한 채 V1 추가, 같은 이름 V2 DLL 변경, 제거 모두 `applied` 응답 확인. V1 SHA-256 `8C044740E872F4CFF2140D69CE5CD9F5FAD70432849660B1B6C67D2D679EB3BE`, V2 `C59920406FC42A357C9ACE9777644F2C1A2B1980AF06776D22C4D73D7143CE4F`. 임시 catalog만 제거하고 사용 중일 수 있는 immutable payload/로드 사본은 보존. 테스트는 모델 수정 및 probe 명령 실행을 하지 않음.
- Native 요청: V1 `48bb2a16c88946f19eacfe0cb305c7d7`, V2 `6f45e05fbda14d4c92569d51fcf7c0fc`, 제거 `01651904b9b64dbca2fdfb4b157be22f`. 제거 후 선택 모듈 0개. 기존 사용자 Revit 세션 유지.

사용 흐름: Manager에서 pyRevit 버튼 설치/제거 선택 → 준비 상태 → Revit의 `RoBIM TEST → RoBIM 연결 TEST → 적용 및 Reload`. 연결 버튼을 처음 설치했으면 표준 pyRevit Reload로 먼저 표시한다. 연결 버튼은 준비를 적용하고 `sessionmgr.reload_pyrevit()`을 호출한다. 표준 Reload만으로 Manager 준비 파일이 적용됐다고 간주하지 않는다.

남은 사용자 환경 확인: 실제 pyRevit 리본 Reload 후 선택한 버튼의 숨김/재표시, 시트 창 및 실제 프로젝트 시트 생성. SDK 빌드나 Manager 테스트로 이를 완료했다고 표현하지 않는다. 이미 로드된 DLL/EXE/C# 내용 변경은 계속 Revit 종료 후 설치 대상이다.

배포 확인: Portable 임시 설치/재설치/렌더/제거/보존/패키지 검사 10/10. 사용자 설치본 Preview 11 업데이트 완료, 배포 EXE와 설치 EXE SHA-256 일치. Manager만 정상 종료 후 다시 실행하고 Revit 세션은 유지. 이전 설치와 이번에 수정한 소스 파일은 workspace artifacts에 백업. Native 검증 기록은 기존 draft PR #1에 반영; Manager의 기존 미커밋 작업은 섞어 커밋하지 않음.
