# 검증 기록 · 2026-10-29

새 TEST 배포. 기존 정식 asset·태그는 변경하지 않습니다. 후보 조회 버튼은 제거하고 레이어 정리·도면 분할·블록 분해·XCLIP 절단을 독립 패키지로 분리했습니다.

Manager 엔진 242/242, 카드 UI 252개, 계정 UI 6개, 역할 UI 19개, Workspace 단위 11개와 공용 배포 정책 13개 검사가 통과했습니다. 실제 AutoCAD Core Console의 모델/Reload 검사 43개가 통과했습니다.

미해결: XclipConcavePolygonAndCircularBoundaryHatch 사례는 AutoCAD를 결과 보고 전 종료시킵니다. 분리 전 기준 commit과 분리 후 코드 모두 같은 사례에서 재현됐습니다. 원인은 확정하지 않았고 이 사례를 통과로 계산하지 않습니다. 전체 AutoCAD GUI 클릭·작업공간·새 TEST 설치 UI 검증은 별도입니다.

설치 순서: TEST Manager → AutoCAD 종료 → TEST CAD 로더 → 독립 기능 선택 → AutoCAD 시작 → ROBIMRELOAD. 저장소 팀 접근 권한 적용은 별도 확인이 필요합니다. 기존 저장소 main 병합과 정식 승격은 보류합니다.
