# Preview 15 — 기존 설치 정리와 선택 프로필 활성화 복구

- pyRevit CLI가 Git 설치 확장만 찾아 Manager의 복사형 선택 프로필을 찾지 못하는 환경을 확인했다. CLI 실패 또는 성공 후 실제 활성 플래그 불일치 시, 알려진 RoBIM 프로필에 한해 pyRevit 표준 INI의 disabled 플래그를 갱신한다. 다른 설정을 보존하고 중복/변경된 설정은 거부한다. UTF-8 BOM 및 줄바꿈을 보존한다. pyRevit 본체를 수정하지 않는다.
- 선택 프로필을 만들 때 카탈로그에 없는 TEST 탭/패널도 명시적으로 빈 layout을 지정한다. 확장 루트의 layout만으로 숨기지 못하는 pyRevit 환경을 보완한다. 코드·DLL·공용 라이브러리는 보존한다.
- 사용자 요청으로 기존 RoBIM Git 확장 1,056개 파일을 백업·해시 검증한 뒤 활성 확장 경로 밖으로 이동했다. Manager 소유권으로 운영 버튼 35개를 설치·활성화했다. CAD는 기존 Manager 관리 번들 하나를 유지했다. 다른 회사 Add-in과 pyRevit 본체는 변경하지 않았다.
- Native Host는 일반 Windows 실행으로 시작한 Revit 2026.5에서 Idling 등록 및 AddInLoadFailureMessage NoError를 확인했다. 앞선 세션의 WrongAssembly 오류 원인을 확정하지 않았으므로 중복 설치가 원인이라고 단정하지 않는다.

검증 결과와 실제 Revit 최종 상태는 작업공간 artifacts의 manager-preview15-update-result.json에 기록한다. 버전 선택·드래그앤드롭 리본 편집은 별도 설계 단계이며 이번 버전에 포함하지 않았다.

실제 최종 확인의 제한: journal.0307에서 Manager 선택 프로필의 운영 버튼 35개와 TEST 버튼 9개가 생성되었다. 구형 RoBIM 프로필 버튼은 0개이고 Native Host는 NoError였다. TEST의 sentinel layout은 파일에 기록되지만 현재 pyRevit 환경의 실제 UI에서는 반영되지 않았다. 단위 검사 통과를 실제 TEST 숨김 성공으로 표현하지 않는다. TEST 버튼은 별도 구형 설치가 아니라 Manager 관리 프로필 안에 남은 구성이다. 버전 선택·리본 편집 단계에서 이 호환성 문제도 다뤄야 한다.
