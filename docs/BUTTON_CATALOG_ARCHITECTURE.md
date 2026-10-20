# 버튼별 Manager 구성 검토 · Preview 10

## 결정

패키지 카드 대신 실제 명령/버튼 한 개를 카드 하나로 표시합니다. 이름, 기능 설명, 아이콘, 제품·분류, 선택/적용 상태, 설치/제거가 기본입니다. 공용 패키지 업데이트·복원·제거는 하위 관리 메뉴로 분리합니다. 오른쪽 업데이트 안내는 없습니다.

버튼마다 저장소를 나누지 않습니다. 공통 로더와 라이브러리가 있는 구조에서 저장소를 버튼 단위로 쪼개면 중복 파일, 버전 조합, 복원 순서가 복잡해집니다.

| 저장소 | 소유 범위 | 이번 구성 |
| --- | --- | --- |
| RoBIM-Add-in-Manager | 카드 UI, 검색/분류, 메타데이터 읽기, 다운로드·설치 선택·백업, Reload 요청·응답 | 명령별 카탈로그 및 선택 상태 추가. Autodesk SDK 참조 없음 |
| RoBIM-CAD | 고정 CAD 로더, 기능 DLL, robim-cad.json | 기존 module/actions/description/icon 구조 재사용. 저장소 분리나 CAD API 재작성 불필요 |
| RoBIM-Revit-Addin | 고정 Revit Host, 모듈 프로토콜, 독립 기능 DLL, 모듈별 ZIP | Host/Reload 프로젝트와 robim-module.json 추가. Sheets는 command-only 모듈로 제공 |
| RoBIM-pyrevit | 기존 버튼 script/bundle.yaml/icon 및 공용 lib/dll | 원본 수정 없이 읽기. 선택 설치는 별도 관리 프로필로 제공. 독립 C# 모듈로 자동 전환하지 않음 |

## 버튼 발견

- CAD: 현재 패키지 또는 검증한 최신/동봉 패키지의 robim-cad.json → module의 각 action을 카드로 표시합니다. 설명/명령/아이콘을 그대로 사용합니다.
- 기존 Revit: 활성 운영 layout을 따라 .tab/.panel/.stack 등과 .pushbutton/.linkbutton 등의 메타데이터를 읽습니다. 한국어 title, 다국어 title, tooltip 블록을 해석합니다. script.py나 DLL을 실행하지 않습니다. Archive·TEST·운영 layout 밖의 미공개 기능을 자동 승격하지 않습니다.
- 독립 Revit: robim-module.json의 각 command를 표시합니다. Git 등록 패키지와 로컬 ZIP 가져오기를 지원하고 여러 명령을 포함한 모듈도 선택별로 관리합니다.

로컬 운영 Revit 사본에서 35개 버튼의 실제 메타데이터를 읽었습니다. 이 개수는 해당 소스의 현재 운영 layout 기준이며 사용자 설치 상태 전체를 의미하지 않습니다.

## 설치/제거 의미

- CAD는 버튼 키별 선택 집합을 준비합니다. 제거는 해당 명령의 등록을 해제하며 공용 로더·다른 기능 파일은 유지합니다. 실제 적용은 CAD Reload 응답으로 확인합니다. 연결 로더 변경은 AutoCAD 종료 후 갱신합니다.
- 기존 pyRevit은 원본을 읽어서 전체 의존 트리를 보존한 RoBIM-Selected.extension 관리 사본을 만들고 선택한 버튼만 layout에 등록합니다. 삭제된 버튼의 코드가 공용 의존성일 수 있어 파일을 무조건 지우지 않습니다. 빈 선택에 layout: []를 쓰면 pyRevit이 전체를 표시하므로 비일치 sentinel을 사용합니다. 원본 Git 사본과 branch/수정은 보존하며 선택 설치의 로컬 수정은 작업을 차단합니다. Preview 11부터 실행 중에는 선택을 준비하고 Revit의 `RoBIM TEST → RoBIM 연결 TEST → 적용 및 Reload`에서 적용 후 표준 pyRevit Reload를 호출합니다. pyRevit core는 수정하지 않습니다.
- 최초 선택 사본은 현재 설치된 운영 원본에서 복사하며 DLL/EXE/C# 등 비스크립트 파일은 원본과 동일해야 합니다. 기존 선택 사본의 바이너리 변경은 재시작 대상으로 거부합니다. 원본/준비 파일/현재 설치 해시와 세션을 재검사하고, 활성화 실패 시 파일과 프로필을 복원하며 요청을 남겨 재시도할 수 있습니다. 선택 준비 중 다른 패키지/프로필 준비로 요청을 덮어쓰지 않습니다.
- 독립 Revit은 공용 Host와 독립된 모듈 패키지를 사용합니다. 준비한 선택과 새 DLL은 기존 실행 경로를 덮어쓰지 않고 Revit API 컨텍스트에서 Reload합니다. 초기 새 로더의 .addin 등록은 실행 중 로딩을 허용하고, 이미 로드된 Host 자체의 변경은 재시작을 요구합니다.
- 설치 준비, 제거 준비, 실제 적용 완료를 구분합니다. 매니저에서 요청을 썼다는 이유로 적용 완료로 표시하지 않습니다.

## 배포

- Manager 빌드는 검증한 CAD ZIP과 Revit Host ZIP/SHA-256을 입력받습니다. 다른 저장소의 빌드·설치 스크립트를 Manager에서 실행하지 않습니다.
- native 모듈 ZIP은 별도 배포/가져오기로 관리하며 SHA-256·경로·매니페스트·의존 파일을 확인합니다. 모듈이 처음 목록에 들어와도 자동 설치하지 않습니다.
- 새 패키지의 기존 선택 기능은 선택을 유지하며 업데이트를 준비합니다. 새 기능은 사용자가 설치를 선택해야 합니다. 폐기된 명령 ID가 있으면 자동 제거 대신 검토를 요구합니다.
- NativeProtocol.cs 계약 사본을 Manager.Core와 Revit.Reload에서 동기화합니다. 서로 프로젝트 참조를 만들지 않습니다. JSON 스키마 변경 시 양쪽 검증을 함께 수정해야 합니다.
- Manager/Host/기능 코드를 한 DLL로 합치지 않습니다. Host를 작게 유지해야 일반 기능 수정 때 Revit을 재시작하지 않을 수 있습니다.

## 남은 현장 확인

실제 Revit GUI의 최초 자동 로드, 신뢰 대화상자, Manager/리본 Reload, 독립 시트 창 실행과 추가/변경/제거는 SDK 컴파일·별도 DLL 격리 테스트와 구분해 확인해야 합니다. 기존 pyRevit 버튼은 계속 pyRevit 기반이며 독립 모듈 변환은 버튼별 후속 개발 범위입니다.
