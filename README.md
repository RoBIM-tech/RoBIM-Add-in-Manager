# RoBIM Add-in Manager

> 신규 Revit 기능은 독립 Add-in으로 개발합니다. 기존 pyRevit은 유지보수하며, 신규 명령은 Manager 등록과 TEST/실사용 구분을 포함해야 합니다. [개발 시작 안내](https://github.com/gagaggan/RoBIM-Workspace/blob/main/docs/developer/NEW_BUTTON.md)

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

[확정 운영 기준](https://github.com/gagaggan/RoBIM-Workspace/blob/main/docs/developer/BUTTON_REPOSITORY_REVIEW.md)

## 단일 버튼 설치 UI

신규 독립 Revit 도구 이름에는 RoBIM 접두어를 반복하지 않는다. 내부 저장소·package/module/command ID와 원격 메타데이터는 보존한다. 중앙 등록된 단일 버튼은 패키지가 없는 첫 화면부터 설치하기를 제공하고, 별도의 기능 목록 보기나 공용 패키지 설치를 선행시키지 않는다. 설치에서 다운로드·SHA-256·단일 명령/등록 ID·배포 상태를 자동 검사하며, 기존 캐시도 폴더명이 아닌 모듈 ID로 연결한다. 새 설치는 기본 탭/패널에 자동 배치하되 저장된 사용자 위치·이름·묶음·다른 버튼을 보존한다.

## TEST에서 정식 배포로 승격할 때

도구의 배포 구분과 PC에 설치된 버전은 별도입니다. 정식 배포 정보로 갱신하면 일반 Revit 목록에 표시하되, 이전 TEST가 설치되어 있으면 그 버전과 정식 업데이트 안내를 함께 표시합니다. 카드의 업데이트 확인은 저장소의 최신 표시 정보를 다시 확인하고 채널에 맞는 검증된 패키지를 받습니다. TEST 캐시를 정식으로 재표시하거나 설치된 채널을 자동 교체하지 않습니다. 업데이트를 선택하고 Reload한 뒤 정식 버전 적용을 확인합니다. 제거는 원격 조회와 채널 일치에 종속시키지 않습니다.

## 버튼 제작과 기여

[신규 버튼 제작 순서](https://github.com/gagaggan/RoBIM-Workspace/blob/main/docs/developer/NEW_BUTTON.md)와 [매니페스트·의존성·배포 계약](https://github.com/gagaggan/RoBIM-Workspace/blob/main/docs/developer/BUTTON_CONTRACT.md)을 따릅니다. 새 버튼은 전용 저장소에서 만들고 Manager 중앙 목록과 Workspace 소유권 맵에 함께 등록합니다. 화면 이름에는 RoBIM 접두어를 반복할 필요가 없습니다. 내부 ID는 이름·버전·저장소 이동 때문에 바꾸지 않습니다.

설치는 카드의 설치하기에서 다운로드·검증·준비로 이어지며 기본 리본 위치를 자동 추가합니다. 기존 사용자 배치는 보존합니다. TEST → 정식은 새 배포와 검증 기록이 필요하고, 소스 병합·Release 게시·PC 설치·호스트 applied 응답은 각각 별도로 확인합니다.

## 새로고침과 업데이트 표시

카드의 새로 고침은 로컬 설치 상태와 원격 기본 브랜치의 배포 정보·채널별 최신 버전을 확인합니다. 주황은 업데이트 가능, 연녹색은 설치/선택됨, 회색은 미설치이며 상태 문구도 함께 표시합니다. 업데이트가 있으면 버튼이 업데이트로 바뀌고 설치 버전 → 제공 버전을 표시합니다. 업데이트 먼저 정렬도 제공합니다. 업데이트를 누르면 검증된 패키지를 준비하고 실행 중인 Revit에 자동 적용을 요청합니다. CAD는 AutoCAD Reload를 사용합니다. 새로고침 자체는 패키지 다운로드·설치·모델 변경을 하지 않습니다.

시작·탭 전환·로컬 타이머는 네트워크를 조회하지 않습니다. 저장소 조회 실패 시 기존 설치와 마지막 확인 결과를 유지하고 최신 확인 실패를 표시합니다. 비교할 수 없는 외부/동봉 버전은 임의로 업데이트라고 판정하지 않습니다. Native는 실제 선택 모듈 버전, 기존 pyRevit은 선택 스냅샷의 브랜치·commit, CAD는 기록된 배포 버전을 비교합니다. 정식 설치에 TEST를 자동 제공하거나 새 버전을 자동 설치하지 않습니다.

Manager의 Revit 설치·업데이트·제거 및 리본 편집의 Revit 적용은 별도 Reload 클릭 없이 자동 적용을 요청합니다. Native Host는 세션 ID를 확인해 Idling에서 처리하고, 기존 pyRevit은 연결 확장의 ExternalEvent에서 적용·Reload합니다. 실행 중인 명령·모달 창이 끝나면 처리하며, 최초 Host 설치나 고정 로더/pyRevit 바이너리 교체는 기존 재시작 조건을 유지합니다. 요청 전송과 실제 applied 응답을 구분하고 다른 버튼의 캐시 업데이트는 자동으로 끌어오지 않습니다. 수동 Reload는 재시도용입니다.
