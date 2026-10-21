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
