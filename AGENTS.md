# RoBIM Add-in Manager

- 이 저장소는 매니저·설치 프로그램·Reload 연결·사용설명서만 관리한다. CAD 애드인 소스는 gagaggan/RoBIM-CAD, pyRevit 도구 소스는 gagaggan/RoBIM-pyrevit, 독립 Revit 모듈은 gagaggan/RoBIM-Revit-Addin에서 관리한다.
- Windows x64 / .NET 10. 빌드: build-manager.ps1. CAD는 별도 Releases의 ZIP/SHA-256을 받아 동봉하며 Autodesk SDK나 CAD 소스를 참조하지 않는다.
- 첫 화면은 Revit / AutoCAD로 분리하고 기본 작업 버튼은 하나만 제공한다. Git URL·계정/PAT·개발자는 보조 기능이다.
- 일반 설치·삭제·복원은 해당 호스트 종료를 요구한다. 기존 설치·로컬 변경·백업·해시·경로 검증을 유지한다.
- Revit 실행 중에는 준비만 하고 단일 Revit 세션 PID·시작 시각을 확인한 내부 연결 버튼에서 적용한다. DLL/EXE/CS 변경은 재시작 대상으로 차단한다.
- pyRevit 원본·기존 Revit 작업 사본에 연결 코드를 주입하지 않는다. Git 기존 사본은 현재 추적 브랜치의 fast-forward만 허용한다.
- PAT는 검증 후 Windows 자격 증명에 저장한다. 인증정보를 설정·로그·명령줄·Git·pyRevit INI에 기록하지 않는다.
- 저장소 빌드/설치 스크립트를 실행하지 않는다. 패키지 URL·토픽·매니페스트를 확인한다.
- tests/run-manager-tests.ps1과 tests/test-manager-installer.ps1은 임시 경로를 사용한다. 사용자 모델을 검증용으로 변경하지 않는다.
- bin/obj/artifacts/.dotnet-home 및 인증정보를 커밋하지 않는다. 브랜치 접두사는 codex/를 사용한다.

## Manager workspace ownership

- Read `.codex/skills/robim-manager/SKILL.md` for this repository. The global Codex copy is installed from this source.
- Shared contracts and compatibility are managed by `gagaggan/RoBIM-Workspace`; do not copy product implementation into that repository.
- Keep user ribbon choices and command IDs stable. Use the Manager workflow in README.md rather than old direct-install instructions.

## Documentation audiences

- User procedures belong in docs/user/README.md and its feature guides; describe inputs, actions, results and recovery.
- Implementation, data flow, lifecycle, build/test and repository design belong in docs/developer/README.md and its references.
- Keep README.md as an entry point to both audiences. Label historical design/validation documents and preserve existing inbound links with pointers.

## 신규 기능 정책

- 신규 Revit 버튼은 pyRevit에 추가하지 않고 독립 Native Add-in으로 제작합니다. 기존 pyRevit 버튼과 Manager 연결은 유지보수합니다.
- 공용 코드는 [RoBIM-Libraries](https://github.com/gagaggan/RoBIM-Libraries)의 역할별 패키지로 관리하고 정확한 버전을 고정합니다. 실행 중 모든 DLL을 공용 폴더 하나에 덮어쓰지 않습니다.
- 누가 제작하든 신규 명령은 Manager `catalog/modules.json`에 등록합니다. 기능 PR과 Manager 등록 PR을 연결하고 배포 전에 등록 검사를 통과해야 합니다.
- 새 버전은 TEST로 시작합니다. 실사용 승격은 실제 호스트 빌드·검증자·검증 기록을 명시한 별도 배포입니다. 설치 성공·main 병합·빌드 성공은 실사용 검증을 대신하지 않습니다.
- [신규 버튼 제작 설명서](https://github.com/gagaggan/RoBIM-Workspace/blob/codex/user-developer-guides/docs/developer/NEW_BUTTON.md)에서 clone부터 생성·패키징·등록·검증까지 진행합니다.
