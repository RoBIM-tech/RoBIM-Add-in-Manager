# RoBIM-tech 조직 이전 — 2026-10-27

## 완료 상태

- 개인 계정 gagaggan의 RoBIM 저장소 15개를 RoBIM-tech로 Transfer ownership 방식 이전했습니다.
- 저장소 ID, 이름, 공개/비공개(14개/1개), 기본 브랜치, 모든 조회 브랜치 커밋, Release 45개와 asset ID/이름/크기/digest 메타데이터, 토픽을 이전 전 기록과 대조했습니다. 기존 ZIP/DLL/nupkg Release 파일을 교체하지 않았습니다.
- RoBIM-tech/robim 팀에 15개 저장소 읽기 권한을 부여했습니다. 조직 전체 기본 권한은 none을 유지합니다.
- 로컬 체크아웃 14개의 origin을 새 조직 주소로 바꿨고 기존 HEAD/작업 브랜치를 유지합니다. 미커밋 파일 64개와 URL 수정 전 143개 파일은 Manager artifacts/repository-transfer-20261027에 보존했습니다. Git에 복구 자료를 추가하지 않습니다.
- 소스/문서/스킬/Workspace URL은 로컬 작업본에서 갱신했습니다. 기존 작업의 자동 main 병합·새 GitHub Release 게시·미커밋 작업 게시를 수행하지 않았습니다.

## Manager

- Manager preview.47의 이전 주소 정규화는 승인된 15개 저장소만 대상으로 합니다. 다른 제작자의 저장소와 미이전 신규 이름은 바꾸지 않습니다.
- 캐시/등록의 namespace는 정규화하고 기존 도구 레코드 ID와 moduleId/commandId는 유지합니다. 기존 개인 제작자 검색 범위는 새 조직으로 읽기 시 이행하되 사용자 파일과 명시적 조직 비활성 선택은 유지합니다.
- 이전 전 개인 URL의 비공개 API 요청을 새 조직 경로로 정규화하여 인증 리다이렉트 문제를 피합니다.
- 엔진 224/224, 소유권 이행 전용 5개, Workspace 24개 검사를 통과했습니다.
- Windows 일반 사용자 일회성 작업에서 실제 경로 검증을 통과한 후 검색했습니다. 로그인 gagaggan, 검색 저장소 10개, 독립 Native 버튼 8개, 경고 0, 이전 소유자 항목 0을 확인했습니다.
- 실제 Manager를 preview.43에서 preview.47로 업데이트하고 알림 영역에서 시작했습니다. 팀 인증 true, 실제 설치 카드 12개 중 Native 8개, 메타데이터 오류 0, SelectionUnchanged true를 확인했습니다. 정식 자동 시작 등록은 유지하며 임시 검사 예약 작업과 실행 스크립트는 제거했습니다.
- Revit/CAD 호스트 DLL과 모델은 교체하지 않았습니다. 고정 Host6/Loader test.2의 새 인증·로그인 전 Reload 정책은 호스트 종료·교체·재시작 검증이 별도로 남아 있습니다.

## 연결 권한과 남은 항목

- 로컬 Git/GCM과 Manager는 새 조직의 비공개 저장소를 읽을 수 있습니다.
- 이 채팅의 GitHub 연결은 로그인 gagaggan이지만 설치 목록에는 개인 계정 설치만 존재합니다. 새 조직 비공개 Workspace 조회는 404였습니다. ChatGPT/Codex GitHub 앱을 RoBIM-tech에도 승인해야 연결 도구 접근이 복원됩니다. 해당 제공자 조직 승인은 자동 변경하지 않았습니다.
- 기본 브랜치의 과거 링크는 GitHub 이전 리다이렉트로 유지합니다. 옛 gagaggan/동일이름 위치에 새 저장소를 만들면 리다이렉트가 없어질 수 있으므로 재생성하지 않습니다.

## 저장소

| 저장소 | 공개 범위 |
| --- | --- |
| [RoBIM-Add-in-Manager](https://github.com/RoBIM-tech/RoBIM-Add-in-Manager) | 비공개 |
| [RoBIM-CAD](https://github.com/RoBIM-tech/RoBIM-CAD) | 비공개 |
| [RoBIM-Libraries](https://github.com/RoBIM-tech/RoBIM-Libraries) | 비공개 |
| [RoBIM-pyrevit](https://github.com/RoBIM-tech/RoBIM-pyrevit) | 비공개 |
| [RoBIM-pyrevit-extension](https://github.com/RoBIM-tech/RoBIM-pyrevit-extension) | 공개 |
| [RoBIM-Revit-Addin](https://github.com/RoBIM-tech/RoBIM-Revit-Addin) | 비공개 |
| [RoBIM-Revit-AutoJoin](https://github.com/RoBIM-tech/RoBIM-Revit-AutoJoin) | 비공개 |
| [RoBIM-Revit-CentralClone](https://github.com/RoBIM-tech/RoBIM-Revit-CentralClone) | 비공개 |
| [RoBIM-Revit-ParameterBulkEdit](https://github.com/RoBIM-tech/RoBIM-Revit-ParameterBulkEdit) | 비공개 |
| [RoBIM-Revit-ParameterCopy](https://github.com/RoBIM-tech/RoBIM-Revit-ParameterCopy) | 비공개 |
| [RoBIM-Revit-RelinkPaths](https://github.com/RoBIM-tech/RoBIM-Revit-RelinkPaths) | 비공개 |
| [RoBIM-Revit-Sheets](https://github.com/RoBIM-tech/RoBIM-Revit-Sheets) | 비공개 |
| [RoBIM-Revit-ViewDuplicate](https://github.com/RoBIM-tech/RoBIM-Revit-ViewDuplicate) | 비공개 |
| [RoBIM-Revit-ViewRotation](https://github.com/RoBIM-tech/RoBIM-Revit-ViewRotation) | 비공개 |
| [RoBIM-Workspace](https://github.com/RoBIM-tech/RoBIM-Workspace) | 비공개 |

## 2026-10-28 게시 준비 갱신

위의 완료 상태는 2026-10-27 이전 직후의 기록입니다. 이후 사용자 요청에 따라 robim 팀 권한을 15개 저장소 모두 관리자(Admin)로 변경했습니다. 2026-10-28 일반 Windows에서 Host preview.6와 CAD Loader test.2를 설치했고 Manager는 설정 화면을 정리한 preview.48로 업데이트했습니다. 설치 기록·Native/CAD 적용 정보·Revit 리본·자동 시작을 유지했으며 자동 로그인과 팀 권한 응답을 확인했습니다. 실제 모델 명령·로그아웃 상태 Reload 검증은 별도입니다. 현재 코드/TEST 패키지 게시를 준비하며 최종 원격 상태는 PR과 Release 기록으로 확인합니다.
