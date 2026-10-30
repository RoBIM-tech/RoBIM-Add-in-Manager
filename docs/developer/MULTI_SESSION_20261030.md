# 여러 실행 창 적용 · TEST

Manager 1.1.1-test.1 / Revit Host 1.0.1-test.1 / CAD Loader 1.1.1-test.1을 함께 사용한다. 기존 정식 1.1.0은 유지한다.

Revit 도구 설치·업데이트·제거는 실행 중인 지원 Revit 2026.5+ 전체에 같은 도구 구성을 준비한다. 각 Host는 캡처한 PID와 시작 시각이 일치할 때 대기 상태에서 적용하며, 실행 중 명령·열린 RoBIM 모델리스 창은 기다린다. 새로 시작한 Revit은 저장된 최신 구성을 읽는다. 지원하지 않는 Revit 버전은 대상에서 제외한다.

AutoCAD 도구 변경과 리본 적용은 여러 창이 열려 있어도 준비한다. 기본 대상은 전체이며 각 창에서 RoBIM → 관리 → Reload를 누른다. 기존 CAD 버튼 선택 화면에서는 특정 창을 선택할 수도 있다. 고정 Host·Loader 자체의 최초 설치/교체/제거에는 해당 프로그램을 모두 종료해야 한다.

Manager는 현재 실행 중인 창별 완료·대기·실패 수와 PID를 표시한다. 한 창의 성공을 전체 성공으로 표시하지 않는다. 실패한 창은 원인을 해소한 뒤 그 창의 Reload로 재시도한다. 다른 창의 성공 기록과 불변 실행 복사본을 보존한다. 프로그램을 재시작하면 이전 PID의 성공 응답을 현재 적용으로 재사용하지 않는다. 구형 Host가 섞여 있으면 새 Host로 연결된 창의 리본 요청을 계속 보내고 구형 창은 연결 업데이트 필요로 표시한다.

구현: NativeRequest의 선택적 Targets는 PID/시작 시각 목록이며 단일 대상 계약도 유지한다. Native 모듈 응답은 Results/<request>-<pid>-<ticks>.json이다. 리본 요청은 revit-ui-requests/<pid>-<ticks>.json, 응답은 revit-ui-results/<request>-<pid>-<ticks>.json으로 분리하고 공용 batch index는 기대 대상만 기록한다. CAD 응답도 Results/<request>-<pid>-<ticks>.json으로 분리한다. 구형 CAD 응답은 해당 PID/시작 시각/요청 ID가 일치할 때만 인정한다. 공유 NativeProtocol 사본은 동일하게 유지한다.

빌드·자동 회귀·실제 사용자 PC 설치·두 실제 GUI 프로세스 적용은 각각 별도 검증 단계다. 이 TEST는 기능 알고리즘이나 사용자 모델을 수정하지 않는다.

검증(2026-10-30): Manager 엔진 278개, self-contained 화면 237개, 권한 UI 19개, 계정 UI 6개 및 Portable/Setup 설치·업그레이드·제거·파일 보존 통과. Revit 실제 SDK 빌드, 프로토콜 15개·리본 계획 17개·모델리스 수명주기 7개 통과. CAD 실제 SDK 빌드와 AutoCAD Core Console 로더 관련 회귀 6개 통과(두 런타임의 성공·부분 실패·재시도 포함). 이는 두 실제 GUI 프로세스의 최종 적용 확인과는 별도이며, 실제 PC 결과는 전달 기록에서 명시한다.
