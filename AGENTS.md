# RoBIM Add-in Manager

- 이 저장소는 매니저·설치 프로그램·Reload 연결·사용설명서만 관리한다. CAD 애드인 소스는 RoBIM-tech/RoBIM-CAD, pyRevit 도구 소스는 RoBIM-tech/RoBIM-pyrevit, 고정 Revit Host는 RoBIM-tech/RoBIM-Revit-Addin, 기능 버튼은 각 전용 저장소에서 관리한다.
- Windows x64 / .NET 10. 빌드: build-manager.ps1. CAD는 별도 Releases의 ZIP/SHA-256을 받아 동봉하며 Autodesk SDK나 CAD 소스를 참조하지 않는다.
- 첫 화면은 Revit / AutoCAD로 분리하고 기본 작업 버튼은 하나만 제공한다. Git URL·계정/PAT·개발자는 보조 기능이다.
- 일반 설치·삭제·복원은 해당 호스트 종료를 요구한다. 기존 설치·로컬 변경·백업·해시·경로 검증을 유지한다.
- Revit 실행 중에는 준비만 하고 단일 Revit 세션 PID·시작 시각을 확인한 내부 연결 버튼에서 적용한다. pyRevit 스냅샷의 DLL/EXE/CS 변경은 재시작 대상으로 차단한다. Native command-only 모듈은 별도 격리 Reload 계약을 적용한다.
- pyRevit 원본·기존 Revit 작업 사본에 연결 코드를 주입하지 않는다. Git 기존 사본은 현재 추적 브랜치의 fast-forward만 허용한다.
- PAT는 검증 후 Windows 자격 증명에 저장한다. 인증정보를 설정·로그·명령줄·Git·pyRevit INI에 기록하지 않는다.
- 저장소 빌드/설치 스크립트를 실행하지 않는다. 패키지 URL·토픽·매니페스트를 확인한다.
- tests/run-manager-tests.ps1과 tests/test-manager-installer.ps1은 임시 경로를 사용한다. 사용자 모델을 검증용으로 변경하지 않는다.
- bin/obj/artifacts/.dotnet-home 및 인증정보를 커밋하지 않는다. 브랜치 접두사는 codex/를 사용한다.

## Manager workspace ownership

- Read `.codex/skills/robim-manager/SKILL.md` for this repository. The global Codex copy is installed from this source.
- Shared contracts and compatibility are managed by `RoBIM-tech/RoBIM-Workspace`; do not copy product implementation into that repository.
- Keep user ribbon choices and command IDs stable. Use the Manager workflow in README.md rather than old direct-install instructions.

## Documentation audiences

- User procedures belong in docs/user/README.md and its feature guides; describe inputs, actions, results and recovery.
- Implementation, data flow, lifecycle, build/test and repository design belong in docs/developer/README.md and its references.
- Keep README.md as an entry point to both audiences. Label historical design/validation documents and preserve existing inbound links with pointers.

## 신규 기능 정책

- 신규 Revit 버튼은 pyRevit에 추가하지 않고 독립 Native Add-in으로 제작합니다. 기존 pyRevit 버튼과 Manager 연결은 유지보수합니다.
- 공용 코드는 [RoBIM-Libraries](https://github.com/RoBIM-tech/RoBIM-Libraries)의 역할별 패키지로 관리하고 정확한 버전을 고정합니다. 실행 중 모든 DLL을 공용 폴더 하나에 덮어쓰지 않습니다.
- 신규 명령은 robim-addon 토픽·기본 브랜치의 자체 표시 정보·TEST Release로 검색됩니다. 중앙 목록은 기본 추천/이전 호환용이며 신규 표시의 필수 조건이 아닙니다. 실제 Manager 새로고침 결과를 검증합니다.
- 새 버전은 TEST로 시작합니다. 실사용 승격은 실제 호스트 빌드·검증자·검증 기록을 명시한 별도 배포입니다. 설치 성공·main 병합·빌드 성공은 실사용 검증을 대신하지 않습니다.
- [신규 버튼 제작 설명서](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NEW_BUTTON.md)에서 clone부터 생성·패키징·등록·검증까지 진행합니다.


## 버튼별 저장소 운영

신규 Revit 버튼은 **버튼 하나 = Git 저장소 하나 = Native 모듈 하나 = 설치 패키지 하나**로 운영합니다. 다른 버튼은 새 저장소에서 만들며 버튼별 장기 브랜치로 나누지 않습니다. codex/ 브랜치는 해당 저장소 안의 수정 작업용입니다. 공유 구현은 RoBIM-Libraries의 역할별 NuGet으로 제공하고 소비 버전을 정확히 고정합니다. 공용 Host/Loader/Manager를 버튼마다 복제하지 않습니다.

모든 새 버튼은 자체 배포 표시 정보·robim-addon 토픽·Workspace 소유권을 제공하고 TEST부터 시작합니다. 중앙 추천 목록 등록은 선택 사항입니다. README에는 일반 사용자/개발자 설명서와 전용 스킬을 연결합니다. 기존 CAD는 유지하고 pyRevit 저장소는 이력만 보관이며 새 Native 버튼을 추가하는 장소가 아닙니다.

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 검색되거나 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.


새 버튼 제작은 Workspace START_HERE.md를 매번 먼저 읽고 생성기·제작 계약·공통 UI·전용 스킬을 적용합니다. README/사용자·개발자 문서/스킬/실제 Manager 표시와 적용 단계를 함께 확인합니다.


## 제작·수정 공통 확인

신규 버튼과 기존 수정 모두 [명명 기준](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NAMING.md)과 [전달 완료 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/BUTTON_DELIVERY.md)를 먼저 읽는다. 화면 이름은 기능명, 공용 패키지는 RoBIM. 전체 이름을 사용하고 기존 설치/모듈/명령 ID는 유지한다. 사용 가능한 버튼 전달 요청은 로컬 코드 작성에서 멈추지 않고 요청 범위의 저장소 게시·TEST 배포·Manager 검색/설치·호스트 적용까지 진행한다. 신규 검색은 robim-addon 토픽·기본 브랜치 descriptor(moduleId/commandId)·Release·검색 대상/권한으로 확인하며 중앙 등록 PR/Manager 재배포는 필수가 아니다. 이 절차가 오래된 중앙 등록 필수 문구보다 우선한다. README·사용자/개발자 문서·스킬·UI_ADOPTION을 실제 변경에 맞춰 갱신하고 소스/기본 브랜치/Release/Manager 카드/설치/호스트 결과를 구분해 보고한다. 게시·병합·설치는 현재 요청 범위를 따르며 미수행 단계를 완료라고 기록하지 않는다.


## RoBIM pyRevit 사용 종료

RoBIM은 독립 Native 버튼으로 운영합니다. 기존 기능과 Python 리본 연결은 [Native 전환 절차](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/PYREVIT_RETIREMENT.md)에 따라 백업·검증 후 제거합니다. NativeOnly에서는 Host가 리본/Reload를 담당하며 pyRevit 본체·다른 확장은 유지합니다. 고정 Host 교체는 Revit 종료가 필요합니다. 새 PC 적용·재시작 후 검증 전에는 완료로 기록하지 않습니다.

## 신규 개발자 온보딩과 현재 계약 (2026-10-28)

- 처음 참여하면 https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/START_HERE.md 에서 환경 준비부터 확인합니다.
- 신규 Revit 버튼은 자체 robim-package.json(moduleId/commandId)·robim-addon 토픽·TEST Release·GitHub 접근 권한으로 검색합니다. Manager 중앙 카탈로그는 기존 등록/추천/이전 호환 경로이며 신규 버튼에 필수는 아닙니다.
- 기존 영구 ID·설치·사용자 리본·정식/TEST 채널을 보존하고 검색, ZIP 검증, 설치 준비, 실제 Host applied 및 모델 실행을 각기 검증합니다.

## Native-only 운영 전환 (2026-10-28)

- RoBIM은 pyRevit을 사용하지 않습니다. 기존 `RoBIM-pyrevit`, `RoBIM-pyrevit-extension` GitHub 저장소는 보관하며 신규 검색·리본·설치·TEST·개발 스킬의 운영 대상에 포함하지 않습니다.
- 이전 pyRevit 로컬 설치·백업·사용자 설정을 자동으로 삭제하지 않습니다. Native Revit Host와 독립 버튼만 사용합니다.
- CAD 패널 오류가 발생하면 Manager 데이터 폴더의 `cad-panel-errors.log`, AutoCAD의 `%LOCALAPPDATA%\\RoBIM\\Cad\\ribbon-errors.log`를 확인합니다. 권한 서비스 장애 시 CAD 리본 명령은 안전하게 비활성화합니다.


## CAD 독립 버튼 TEST 전환 · 2026-10-29

현재 분리 작업은 [전환 계약](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/codex/cad-loader-button-split/docs/developer/CAD_INDEPENDENT_PACKAGES.md)을 따릅니다. CAD 로더 저장소는 RoBIM-CAD-Loader로 변경됐으며 기능 4개는 별도 저장소입니다. 후보 조회는 제외합니다. 기존 설치·명령 ID와 정식 Release는 보존합니다. 새로운 패키지는 TEST이며 실제 호스트 검증과 main 병합·정식 승격은 별도입니다. 이전 통합 CAD 공급 설명보다 이 전환 계약이 우선합니다.
