# Manager preview.48 TEST 게시 기록 — 2026-10-28

설정 화면 정리·팀 로그인·자동 시작·전체 제거·조직 이전 호환 코드를 작업 브랜치에 게시하고, 게시한 소스에서 별도 배포 빌드를 만들었다. 작업 중인 CentralClone 중앙 등록과 해당 등록 전용 이전 alias는 배포 소스에서 제외하고 기존 작업 폴더에 보존했다. 기본 브랜치 병합과 정식 승격은 수행하지 않았다.

## 코드와 패키지

| 구성 | 검토 | TEST 배포 | 배포 소스 커밋 |
| --- | --- | --- | --- |
| Manager preview.48 | [PR #11](https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/pull/11) | [Setup·Portable·SHA-256](https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/v1.0.0-preview.48) | fde0eacbaed8fe4759e5f86fc08820fab2b332bb |
| Access.Core 0.1.0-test.3 | [PR #9](https://github.com/RoBIM-tech/RoBIM-Libraries/pull/9) | [nupkg·SHA-256](https://github.com/RoBIM-tech/RoBIM-Libraries/releases/tag/access-core-0.1.0-test.3) | 2d3b693667e7c5edf4a2413f8f1d076c12dd9fab |
| Revit Host preview.6 | [PR #5](https://github.com/RoBIM-tech/RoBIM-Revit-Addin/pull/5) | [ZIP·SHA-256](https://github.com/RoBIM-tech/RoBIM-Revit-Addin/releases/tag/host-v1.0.0-preview.6) | 29446a5cd44244f19fb5ffd944d34b95afbc54f6 |
| CAD Loader 1.0.1-test.2 | [PR #6](https://github.com/RoBIM-tech/RoBIM-CAD/pull/6) | [ZIP·SHA-256](https://github.com/RoBIM-tech/RoBIM-CAD/releases/tag/v1.0.1-test.2) | 5ce6864ba9e75e0296101a5828430da580b3d90b |

Access PR은 기존 공용 이름 편집 브랜치를 기준으로 인증 패키지 변경을 분리했다. 소비자는 RoBIM.Access.Core [0.1.0-test.3]을 고정한다. 배포 nupkg를 로컬 feed에 내려받아 `build-manager.ps1 -LibraryFeed <feed> -CadPackagePath <검증한 CAD ZIP> -RevitHostPackagePath <검증한 Host ZIP>`으로 빌드한다. Common 등 기존 공용 패키지도 동일 feed 또는 복원 가능한 캐시에 필요하다. 빌드는 존재하는 로컬 캐시만 사용하고 HTTPS 소스를 NuGet 설정 파일로 전달한다.

## 검증

- 게시용 소스의 Manager 엔진 224/224, 관리 리본/퇴역 리본/중앙 목록 회귀 검사 통과.
- 최종 self-contained 실행 파일의 계정 설정 UI 6개, 로그인 잠금 UI 7개, 일반 Windows 사용자 Pipe 8개 통과.
- 최종 Portable·Native Setup의 임시 설치/재설치/제거·외부 파일 보존 검사 통과.
- Revit SDK 빌드와 Reload 회귀 11개, CAD SDK 전체 빌드 통과.
- 원격 Release 4개가 prerelease이고 태그가 위 커밋을 가리킴을 확인했다. 첨부파일 10개의 크기와 GitHub SHA-256 digest가 로컬 파일과 일치한다. 과거 Release 파일은 교체하지 않았다.
- Manager PR의 registration 자동 검사 통과. 검사 근거는 로컬 `artifacts/publish-preview48`에 보존하며 빌드·복구 산출물은 Git에 추가하지 않는다.

## PC 적용과 남은 검증

게시 전 이 PC에 적용한 Manager preview.48과 Host preview.6/CAD Loader test.2의 설치 기록은 TEAM_ACCESS.md와 UI_ADOPTION.md에 있다. 기존 설치/리본/선택을 유지하고 자동 로그인·팀 권한 응답을 확인했다. 게시용 파일은 별도 소스에서 다시 빌드·검증했으며, 사용자 PC의 기존 설치를 이번 게시 작업으로 다시 덮어쓰지는 않았다.

실제 Revit 모델·CAD 도면에서 로그인 전후 기능 명령과 로그인 전 Reload 실행은 별도 검증 대상이다. 설치/빌드/회귀 검사 성공을 실제 모델 작업 검증이나 정식 승격으로 해석하지 않는다.
