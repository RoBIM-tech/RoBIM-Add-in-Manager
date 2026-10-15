**RoBIM Add-in Manager 1.0.0 Preview 5**

매니저를 gagaggan/RoBIM-Add-in-Manager 저장소로 분리했습니다. RoBIM-CAD는 CAD 도구 소스와 CAD 패키지만 관리합니다.

- Revit / AutoCAD 화면을 분리하고 해당 제품 도구만 표시
- 도구 선택 후 기본 작업 버튼 하나로 설치·업데이트 안내
- 제품 종료 여부에 따라 버튼과 다음 단계가 자동 변경
- 새 CAD 설치는 로그인 없이 동봉 패키지 사용 가능
- Revit 기존 설치는 실행 중 업데이트 준비 후 Revit 내부 적용
- 첫 Reload 준비 시 연결 확장이 없으면 자동 설치
- 일반 설치 후 해당 도구의 오래된 Reload 준비 요청 정리
- Git URL 등록은 도구 추가, 인증과 토큰은 설정 / GitHub로 이동
- 창·바로가기·Windows 앱 이름을 RoBIM Add-in Manager로 정리. 기존 설치 경로와 설정·인증·백업은 유지

설치 파일을 실행하기 전에 기존 매니저를 종료하십시오. 매니저 설치 후 Revit 또는 AutoCAD에서 도구를 선택하고 설치 버튼을 누르면 됩니다. CAD DLL과 새 Revit/일반 확장 설치는 제품 종료가 필요합니다.

설치 엔진·온라인 연동 검증과 Setup 검증 결과는 docs/MANAGER_VALIDATION.md를 참고하십시오. 실제 Revit 모델 내 Reload와 신규 유효 PAT 입력은 사용자 환경 확인 범위입니다.

[간단 사용설명서](https://github.com/gagaggan/RoBIM-Add-in-Manager/blob/main/docs/USER_GUIDE_KO.md) · [저장소 분리 기록](https://github.com/gagaggan/RoBIM-Add-in-Manager/blob/main/docs/REPOSITORY_SPLIT.md)
