# 자동 결합 모델리스 TEST 전달 · 2026-10-28

- 자동 결합 0.1.0-test.2: b3fa8fc19176740d15c257297d19a1b1817ba225. 모델리스/행 선택, 뷰 이동·모델 변경 후 결과 보존.
- Host 1.0.0-preview.8: bb637ee7dc5bfb947d59e2b2c23ede8ea2cf81d8. 명시적 창 수명/외부 이벤트 권한/열린 창 Reload 대기.
- Manager 1.0.0-preview.50: 새 Host를 동봉한 배포. 기존 Manager 로직은 변경하지 않는다. CAD Loader는 기존 1.0.1-test.3 유지.

자동 결합 정책/UI 36개, 실제 Revit SDK 빌드/ZIP 검사, Host 수명/권한 7개·Reload 11개·리본 17개는 통과했다. Manager 빌드/임시 설치 및 원격 asset 검증 결과는 수행 후 기록한다. 실제 PC 설치와 Revit 재시작/모델 명령은 미수행이다. 사용자 모델을 시험용으로 수정하지 않는다.

기본 브랜치의 자동 결합 descriptor는 기존 module/command ID와 release/test 경로를 유지하므로 새 TEST Release 검색에 중앙 등록/Manager 기능 로직 변경이 필요하지 않다. Manager 재배포는 이번 기능이 요구하는 고정 Host 업데이트를 전달하기 위해 필요하다.

## TEST 게시 완료

- [자동 결합 v0.1.0-test.2](https://github.com/RoBIM-tech/RoBIM-Revit-AutoJoin/releases/tag/v0.1.0-test.2)
- [Host 1.0.0-preview.8](https://github.com/RoBIM-tech/RoBIM-Revit-Addin/releases/tag/host-v1.0.0-preview.8)
- [Manager v1.0.0-preview.50](https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/v1.0.0-preview.50)

세 배포는 draft=false/prerelease=true다. 태그가 위의 정확한 소스를 가리키는지 확인했다. 원격 파일 8개를 다시 다운로드해 SHA-256과 로컬 검증본의 전체 바이트가 같음을 확인했다. Portable의 동봉 Host ZIP도 별도 Host Release ZIP과 일치한다. 기존 배포 파일은 교체하지 않았다.

Manager 엔진 233/233, 리본 활성화/퇴역 회귀, self-contained 빌드, Portable 임시 설치/재설치/제거/외부 파일 보호와 고유 TestBuild Native Setup 설치/업데이트/제거/외부 파일 보존 검사가 통과했다. 제품 동작 변경 없이 새 Host를 동봉한 배포다.

일반 Windows 사용자 환경의 공식 --inspect-release-offers가 새 자동결합 v0.1.0-test.2와 원격 ZIP digest를 수신했다. SelectionUnchanged=true이고 설치/리본/Native active/pending은 보존됐다. 검사 모드는 실제 계정 초기화를 생략해 TEST 카드를 표시하지 않으므로 TEST 카드의 실제 화면 검증으로 해석하지 않는다. 설치된 Manager 창은 Computer Use에서 열기/관찰되지 않아 실제 TEST 카드와 설치/Host 적용/모델 명령은 미검증으로 남긴다. 사용자 PC 설치나 모델 수정은 수행하지 않았다. 일회성 Windows 읽기 검사 작업은 완료 후 제거했다.

로컬 근거: artifacts/modeless-test-publish/remote-verification.json, remote-tags-and-discovery.json, manager-card-check.json, windows-check-result.json 및 깨끗한 빌드 스냅샷 artifacts/modeless-source-95d7c0f의 검사 결과. 기본 브랜치 병합/정식 승격은 수행하지 않았다. 기존 main의 자동결합 descriptor와 robim-addon 토픽은 확인했다.
