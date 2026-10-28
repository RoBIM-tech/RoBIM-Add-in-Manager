# 자동 결합 모델리스 TEST 전달 · 2026-10-28

- 자동 결합 0.1.0-test.2: b3fa8fc19176740d15c257297d19a1b1817ba225. 모델리스/행 선택, 뷰 이동·모델 변경 후 결과 보존.
- Host 1.0.0-preview.8: bb637ee7dc5bfb947d59e2b2c23ede8ea2cf81d8. 명시적 창 수명/외부 이벤트 권한/열린 창 Reload 대기.
- Manager 1.0.0-preview.50: 새 Host를 동봉한 배포. 기존 Manager 로직은 변경하지 않는다. CAD Loader는 기존 1.0.1-test.3 유지.

자동 결합 정책/UI 36개, 실제 Revit SDK 빌드/ZIP 검사, Host 수명/권한 7개·Reload 11개·리본 17개는 통과했다. Manager 빌드/임시 설치 및 원격 asset 검증 결과는 수행 후 기록한다. 실제 PC 설치와 Revit 재시작/모델 명령은 미수행이다. 사용자 모델을 시험용으로 수정하지 않는다.

기본 브랜치의 자동 결합 descriptor는 기존 module/command ID와 release/test 경로를 유지하므로 새 TEST Release 검색에 중앙 등록/Manager 기능 로직 변경이 필요하지 않다. Manager 재배포는 이번 기능이 요구하는 고정 Host 업데이트를 전달하기 위해 필요하다.
