# RoBIM Add-in Manager

Revit과 AutoCAD 도구를 설치·업데이트하는 Windows 앱입니다. **제품 선택 → 도구 선택 → 설치 버튼** 순서로 사용합니다.

[Preview 6 설치 파일](https://github.com/gagaggan/RoBIM-Add-in-Manager/releases/tag/manager-v1.0.0-preview.6) · [간단 사용설명서](docs/USER_GUIDE_KO.md)

## 사용

- **AutoCAD**: 왼쪽 AutoCAD → DELHIDDEN 선택 → 설치하기. 완료 후 AutoCAD에서 `DHNET` 실행.
- **Revit**: 왼쪽 Revit → 도구 선택 → 설치하기/업데이트하기. Revit 실행 중인 기존 RoBIM은 업데이트 준비 후 Revit 안에서 적용 및 Reload.
- 처음 한 번 필요한 계정 연결은 버튼이 안내합니다. 새 CAD 설치는 로그인 없이 동봉 버전을 사용할 수 있습니다.
- 다른 Git URL은 **도구 추가**, 개인 시험 기능은 **개발자 TEST**, 인증·토큰은 **설정 / GitHub**에 있습니다.

설치 전 제품을 종료해야 하는 경우 버튼 옆 안내가 표시됩니다. 제품을 닫고 돌아오면 버튼이 자동으로 활성화됩니다.

## 저장소 구분

| 저장소 | 내용 |
| --- | --- |
| 이 저장소 | 매니저 UI, 설치 엔진, Setup, Reload 연결, 사용설명서와 매니저 배포 파일 |
| [RoBIM-CAD](https://github.com/gagaggan/RoBIM-CAD) | DELHIDDEN 등 CAD 도구 소스·빌드·CAD 배포 파일 |
| [RoBIM-pyrevit](https://github.com/gagaggan/RoBIM-pyrevit) | Revit 도구 소스·개발 브랜치 |

이 저장소는 CAD 소스나 Autodesk SDK를 빌드하지 않습니다. CAD의 검증된 Release ZIP을 받아 매니저에 동봉합니다. 기존 설치 위치·인증·백업은 업그레이드 시 유지합니다.

## 개발

Windows x64 / .NET SDK 10 / Git for Windows. 비공개 CAD 릴리스 최초 다운로드에는 Git Credential Manager 로그인과 GitHub CLI가 필요합니다.

```powershell
.\build-manager.ps1
.\tests\run-manager-tests.ps1
.\tests\run-manager-tests.ps1 -Online
.\tests\test-manager-installer.ps1
.\tests\test-native-installer.ps1
```

출력은 `artifacts/RoBIM-Manager-Setup.exe`와 `artifacts/RoBIM-Manager-Portable.zip`입니다. 이미 검증된 CAD ZIP/SHA-256이 캐시에 있으면 네트워크 없이 재사용합니다.

Setup은 Inno Setup 6.4.3으로 생성합니다. 최초 빌드는 공식 배포 파일의 고정 SHA-256과 서명을 확인하고 작업 폴더에 컴파일러를 준비합니다. 이 버전의 라이선스는 상업적 사용을 허용합니다. Windows 사용자별 설치·업데이트·전용 제거 프로그램과 앱 목록 등록은 Inno Setup이 관리합니다. PowerShell은 설치 경로·기존 설치 소유권·매니저 실행 여부를 사전 검사하는 용도로만 사용합니다. Portable ZIP의 스크립트는 별도 호환 경로이며 Setup 설치본에는 실행할 수 없습니다.

인증정보는 Git에 올리지 않습니다. PAT는 Windows 자격 증명 관리자에 보관합니다. 기존 파일 변경·외부 설치 덮어쓰기·실행 중 DLL 교체를 차단합니다. 실제 Revit 리본 Reload/모델 내 동작은 사용자 환경에서 확인해야 합니다.

[기술 구조](docs/MANAGER.md) · [패키지 규격](docs/EXTENSION_PACKAGES.md) · [검증 기록](docs/MANAGER_VALIDATION.md)
