# RoBIM Add-in Manager

> 신규 Revit 기능은 독립 Add-in으로 개발합니다. 기존 pyRevit은 유지보수하며, 신규 명령은 Manager 등록과 TEST/실사용 구분을 포함해야 합니다. [개발 시작 안내](https://github.com/gagaggan/RoBIM-Workspace/blob/codex/user-developer-guides/docs/developer/NEW_BUTTON.md)

Revit·AutoCAD의 기능 버튼을 설치·제거하고 Reload와 사용자 리본 배치를 관리하는 Windows 앱입니다.

| 독자 | 시작할 문서 | 설명하는 내용 |
| --- | --- | --- |
| 일반 사용자 | [사용설명서](docs/user/README.md) | 설치·입력·버튼 실행·결과·제거·Reload·문제 해결 |
| 개발자 | [개발자 설명서](docs/developer/README.md) | 구조·진입점·데이터·적용 흐름·실패 복원·빌드·검사 |

[전체 문서 목록](docs/README.md)

## 설치와 개발의 경계

사용자 설치는 RoBIM Add-in Manager에서 진행합니다. 개발자는 설치된 사용자 폴더 대신 이 저장소에서 작업하고 검증된 소스/패키지를 제공합니다. 배포 Release와 현재 검토 작업본의 제공 기능은 다를 수 있습니다.

## 스킬과 연동

전용 스킬 원본은 [`.codex/skills/robim-manager`](.codex/skills/robim-manager/SKILL.md)입니다. 제품 간 계약과 스킬 설치본 동기화는 [RoBIM-Workspace](https://github.com/gagaggan/RoBIM-Workspace)에서 관리합니다. 사용자 리본 표시·설치 선택과 기능 코드의 기본 배치를 구분합니다.


## 버튼별 저장소 운영

신규 Revit 버튼은 **버튼 하나 = Git 저장소 하나 = Native 모듈 하나 = 설치 패키지 하나**로 운영합니다. 다른 버튼은 새 저장소에서 만들며 버튼별 장기 브랜치로 나누지 않습니다. codex/ 브랜치는 해당 저장소 안의 수정 작업용입니다. 공유 구현은 RoBIM-Libraries의 역할별 NuGet으로 제공하고 소비 버전을 정확히 고정합니다. 공용 Host/Loader/Manager를 버튼마다 복제하지 않습니다.

모든 새 버튼은 Manager 중앙 목록과 Workspace 소유권 목록에 함께 등록하고 TEST부터 시작합니다. README에는 일반 사용자/개발자 설명서와 전용 스킬을 연결합니다. 기존 CAD 묶음·기존 pyRevit은 호환성을 유지하는 레거시이며 새 Native 버튼을 추가하는 장소가 아닙니다.

[확정 운영 기준](https://github.com/gagaggan/RoBIM-Workspace/blob/codex/user-developer-guides/docs/developer/BUTTON_REPOSITORY_REVIEW.md)

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 중앙 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.
