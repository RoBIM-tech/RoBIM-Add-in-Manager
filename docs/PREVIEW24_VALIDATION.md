# Preview 24 검증

재현 근거: 등록된 DLL 경로는 LocalAppData/RoBIM/Manager였지만 GetFinalPathNameByHandle 결과는 Packages/OpenAI.Codex.../LocalCache/Local/RoBIM/Manager였다. 동일 사용자의 일반 Windows 일회성 프로세스에서는 등록 DLL이 존재하지 않았다. 이전 정상 확인은 같은 격리 실행 환경에서 시작한 Revit이었으므로 일반 실행 검증으로 볼 수 없다.

변경: Manager 시작 및 Native Host 설치 전에 작은 삭제 자동 임시 파일을 열고 실제 파일 핸들 경로를 확인한다. 다른 물리 경로이면 변경을 중단한다. 확장 경로 접두사 정규화, 정상 임시 데이터 경로 통과 및 프로브 정리, 다른 물리 경로 거부를 회귀 검사에 추가한다. 화면 확인·UI 검사와 기존 창 종료 명령은 설치 데이터를 변경하지 않아 예외로 둔다.

복구: 양쪽 트리를 백업하고 원본 해시와 비교한다. 관리 설치 기록과 명령 파일을 검증한 후 일반 AppData에 누락된 Native Host·모듈·캐시와 최근 설정을 반영한다. 실제 CAD 데이터와 명령 파일은 변경하지 않는다. 세션·Loaded·임시 UI 요청은 승격하지 않는다. 일반 Windows 실행의 실제 파일 경로, Revit 저널의 NoError 및 새 세션 기록으로 검증한다. 일회성 진단·배포 작업은 완료 후 삭제한다.

최종 검사 수, 복구 백업 및 일반 실행 증거는 작업공간 artifacts/manager-preview24-update-result.json에 기록한다. NuGet 취약성 데이터 조회 경고는 빌드·검사 성공과 구분한다.

최종 결과: 엔진 139개, UI 196개, 설치 검사 10개 통과. 일반 Windows 실행의 Revit 2026.5에서 journal.0326 및 journal.0327 모두 Native Host NoError를 확인했다. 각 실행 PID와 일치하는 Native 모듈 적용 영수증을 확인했다. 등록된 DLL의 파일 핸들 경로도 일반 AppData 경로와 일치했다. 복구 전 일반 AppData 3,887개, 앱 전용 데이터 20,192개 파일의 백업·해시를 확인했고, 17,058개 항목을 반영했다. 기존 CAD 데이터와 설치된 명령 파일의 해시는 그대로였다. 사용자 모델 및 명령 기능은 실행하지 않았다.
