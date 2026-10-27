# 저장소 분리 기록

2026-10-15, 사용자 요청에 따라 매니저를 별도 비공개 저장소 **RoBIM-tech/RoBIM-Add-in-Manager**로 분리했습니다.

- 매니저 UI·Core·Setup·Reload 연결·사용설명서: 이 저장소 main
- CAD 소스·Autodesk 참조·CAD 패키지: RoBIM-tech/RoBIM-CAD main
- Revit 도구 소스·개발 브랜치: RoBIM-tech/RoBIM-pyrevit
- 이 저장소는 CAD를 빌드하지 않고 CAD Releases의 검증된 ZIP/SHA-256을 소비합니다.

## 배포 이전

manager-v1.0.0-preview.1 / .2 / .3 / .4의 Setup EXE·Portable ZIP·SHA256SUMS를 원본 크기 및 GitHub SHA-256과 비교한 뒤 새 저장소에 업로드했습니다. 새 업로드의 크기·해시가 일치함을 재검증하고 CAD 저장소의 해당 매니저 릴리스만 제거했습니다. CAD 릴리스 v1.0.0-preview.1은 보존했습니다.

과거 매니저 바이너리는 새 저장소의 동일 태그 릴리스에서 받을 수 있습니다. 이관 릴리스의 소스 이력은 원래 CAD 저장소 Git 태그에 연결했습니다. 현재 매니저 소스는 Preview 5의 별도 초기 커밋에서 시작합니다.

## 이전 개발 이력

CAD 저장소의 PR #1은 병합 없이 종료했습니다. 과거 Git 브랜치·태그는 이력 확인을 위해 보존하며 강제로 다시 쓰지 않았습니다. CAD의 기본 main 체크아웃은 CAD 전용 상태입니다.

## 설치 호환성

이름은 RoBIM Add-in Manager로 바꾸되 기존 Programs/RoBIM Manager 설치 경로·데이터·GCM/PAT·백업·실행 파일 이름과 앱 식별자를 유지합니다. 새 바로가기와 Windows 앱 표시는 새 이름을 사용하며 같은 실행 파일을 가리키는 이전 바로가기만 제거합니다. 사용자 CAD/Revit 모델·기존 도구 소스·활성 프로필은 저장소 분리 과정에서 변경하지 않았습니다.

새 배포: https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/manager-v1.0.0-preview.5
