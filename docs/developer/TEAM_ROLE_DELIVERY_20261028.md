# 팀별 권한 전달 기록 — 2026-10-28

현재 정책은 [TEAM_ACCESS.md](TEAM_ACCESS.md)의 네 팀 기준이다. 로컬 사용자/모델 검증과 GitHub 게시를 구분한다.

| 대상 | 소스 | TEST 배포 |
| --- | --- | --- |
| Manager | db6969b0be76b50909f2b557bec424e1abb60873 | [1.0.0-preview.49](https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/v1.0.0-preview.49) |
| Revit Host | 43858cde5c6b2b133800a600f58ad36b3da0a2a7 | [1.0.0-preview.7](https://github.com/RoBIM-tech/RoBIM-Revit-Addin/releases/tag/host-v1.0.0-preview.7) |
| CAD Loader | c011396de3e401cad1e42c6bce0e8dba1c747686 | [1.0.1-test.3](https://github.com/RoBIM-tech/RoBIM-CAD/releases/tag/v1.0.1-test.3) |
| 공용 권한 소스 | 83af2f0d502218cd4a7497128ea6ac6db05a39b8 | [RoBIM.Access.Core 0.1.0-test.4](https://github.com/RoBIM-tech/RoBIM-Libraries/releases/tag/access-core-0.1.0-test.4) |

네 Release 모두 prerelease이며 이전 버전 파일은 교체하지 않았다. 원격 태그의 소스 커밋과 첨부 파일 10개의 크기·SHA-256을 로컬 검증본과 대조했다. 기본 브랜치 병합과 정식 승격은 수행하지 않았다. 별도 진행 중인 CentralClone 등록은 Manager 릴리스 소스에 포함하지 않았다.

## 검증 결과

- Manager 엔진 233/233: 일반/TEST/개발자/관리자, TEST 역할 회수, 실패한 추가 팀 조회, 일반 사용자의 TEST 가져오기·캐시·자동 준비 차단, 기존 TEST 제거와 정식 전환, Release 채널 선택을 포함한다.
- 최종 self-contained Manager: 정상 Windows 역할 UI 19개, 계정 설정 UI 6개 통과.
- 정상 Windows Pipe 14개: 일반 사용자의 TEST 거부, 두 채널 허용, 권한 회수, 잘못된 비트 거부, 구형 v1 호스트 차단을 포함한다.
- Revit/CAD SDK 빌드, Native Reload 11개 통과.
- 최종 Portable 및 고유 시험용 Native Setup의 임시 설치·업데이트·제거·외부 파일 보존 검사 통과.
- Workspace 검사 24개와 NativeProtocol/CadConfiguration 바이트 일치 확인. 네 제품 스킬 원본을 기존 설치본 백업 후 동기화했다.

## 현재 PC

일반 Windows 사용자 환경에서 실제 경로 검사를 통과하고 Manager preview.49를 Setup으로 설치했다. 설치된 실행 파일의 해시가 최종 배포본과 일치한다. 기존 installed.json, Native/CAD active/pending 및 사용자 리본, 자동 시작 등록을 보존했다. 저장된 gagaggan 계정으로 관리자 역할과 stable+TEST 권한 비트 3을 확인했다. 계정 토큰은 출력하거나 문서에 기록하지 않았다.

이후 사용자의 Revit/CAD 종료 응답을 받고 일반 Windows에서 두 프로세스가 없음을 재확인했다. 관리 중인 기존 Host preview.6/CAD Loader test.2가 수정되지 않았음을 확인한 뒤 백업 후 Host preview.7과 CAD Loader test.3을 설치했다. 설치 파일 전체가 준비된 패키지와 일치하고 Modified=false다. Native active/pending 및 리본 해시는 유지했으며 CAD 버튼 4개를 새 Loader용 pending 요청에 그대로 보존했다. 다음 Revit/CAD 실행부터 새 고정 Host/Loader가 로드된다. 실제 일반 사용자 계정, 실제 모델/DWG 명령의 로그인 전후 실행, Windows 재로그인 자동 시작은 이번에 검증하지 않았다. 다른 작업의 Host preview.8 소스는 설치 대상으로 사용하지 않았다.

로컬 근거: artifacts/role-access-20261028/publish/remote-verification.json, windows-release-verification.json, workspace-check.json, skill-sync.json, artifacts/role-tests-release-20261028/results.json, artifacts/role-access-20261028/apply-manager/installed-result.json 및 current-access.json, artifacts/role-access-20261028/apply-hosts-closed/result.json. 근거 파일은 Git에 포함하지 않는다.
