# Preview 10 로컬 반영 및 검증 · 2026-10-19

- Manager 원본 소스와 사용자 설치본을 Preview 10으로 변경. 이전 소스와 Preview 9 설치본을 workspace artifacts에 백업. 마지막 설치 EXE는 검증한 배포 EXE와 SHA-256 일치.
- 실제 운영 pyRevit 메타데이터 35개 읽기, 35/35 기능 설명 확인. YAML 외의 레거시 __doc__는 문자열 리터럴만 읽음. 설명이 없는 RoomNameCopy/RoomTagTypeChange는 읽은 코드 동작에 맞춘 호환 설명 제공.
- Manager 엔진: 97/97. 버튼 선택, 원본/공용 의존성 보존, 프로필 배타성, 실패 복원, native 캐시 변조·세션·응답·다중 업데이트 원자성·로더 정책 포함.
- 배포 WinForms EXE: UI 61/61. 개별 버튼 여러 개가 같은 패키지에서 표시되는지, 검색/정렬/설치·제거/메뉴/레이아웃/잠금 확인.
- Portable 임시 설치·재설치·렌더·제거·파일 보존·거부 경로·Reload ZIP·Native Host ZIP·라이선스: 10항목.
- 독립 런타임 DLL 격리/버전 교체: 11/11. 시트 엔진 18/18. Host/Sheets/Manager SDK 및 ZIP 빌드 성공.
- 고정 native Host를 일반 Windows 셸 경로에서 사용자 Addins/2026에 등록. Sheets v0.2.0-preview.1은 기능 목록에 가져왔으며 사용자 기능을 자동 설치하지 않음.
- 모델 인수 없이 Revit 2026.5 프로세스를 실행했으나 실제로 `Security - Unsigned Add-In` 확인창에서 대기. Host의 실 Revit applied 응답은 아직 없으므로 실제 GUI Reload 성공으로 기록하지 않음.
- 임시 smoke 버튼은 pending 선택에서 해제하고 전용 catalog-reload-smoke.json 한 파일만 정리. 사용 중일 수 있는 immutable payload/실행 파일은 삭제하지 않음. 사용자 모델은 열거나 수정하지 않음.
- 독립 저장소 브랜치: codex/button-catalog-native-reload, 커밋 72ae6f1. Draft PR: https://github.com/RoBIM-tech/RoBIM-Revit-Addin/pull/1. main/새 릴리스 게시 없음.
- Manager 저장소에는 이전 CAD/native/설치 작업의 미커밋 변경이 있어 이번 변경만 로컬 반영하고, 다른 작업을 섞어 커밋/게시하지 않음.

2026-10-20 후속: 사용자의 Native Host 로드 허용 후 동일 실행 중 Revit에서 probe v1 추가 → 동일 이름 v2 DLL 변경 → 제거 모두 applied 응답을 확인했다. 상세 및 pyRevit 버튼 선택 개선은 PREVIEW11_VALIDATION.md 참조. 위 최초 보안창 대기 기록은 10월 19일 시점의 결과다. 시트 창/시트 생성의 실제 프로젝트 확인은 별도다.
