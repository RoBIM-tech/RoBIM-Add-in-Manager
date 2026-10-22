# Git URL 확장 패키지와 GitHub 연결

Preview 5는 **도구 추가** 화면에서 GitHub Git URL 등록과 연결 계정의 애드인 저장소 자동 조회를 제공합니다. GitHub HTTPS URL 또는 `git@github.com:owner/repo.git`를 입력하면 HTTPS 방식으로 정규화합니다. URL에 토큰을 넣지 않습니다. 다른 Git 서버와 GitHub Enterprise는 현재 지원하지 않습니다.

## 계정과 토큰

**설정 / GitHub** 화면은 GitHub `/user` API로 확인한 실제 ID와 인증 방식(브라우저 계정 / 토큰)을 표시합니다. 브라우저 로그인은 Git Credential Manager에 저장하며, 저장 계정 목록에서 연결할 ID를 선택할 수 있습니다.

PAT는 마스킹된 입력란에 넣고 **토큰 확인 및 저장**을 누릅니다. 서버에서 계정 ID를 검증한 뒤 Windows 자격 증명 관리자에 저장합니다. 입력란은 작업 시작 시 비우며 토큰을 앱 설정·Git 설정·로그·명령줄 인자·pyRevit INI에 기록하지 않습니다. Git 다운로드에는 현재 프로세스의 메모리상 인증 헤더를 사용합니다. 잘못된 토큰은 이전 연결을 변경하지 않습니다. **현재 토큰 삭제**는 RoBIM이 저장한 해당 토큰만 삭제하며 GCM의 브라우저 계정 인증은 보존합니다.

Fine-grained PAT는 사용할 저장소를 선택하고 Metadata / Contents 읽기 권한을 부여하십시오. 앱은 저장소에 코드를 Push하거나 사용자 토큰의 권한을 늘리지 않습니다. 토큰 만료·권한 변경 시 다시 연결해야 합니다. 실제 토큰 없이 저장 기능을 시험할 때는 별도 임시 자격 증명을 사용하고 즉시 삭제합니다.

## 자동 검색에서 일반 저장소 제외

GitHub 저장소 Settings/About의 Topics에 다음 중 하나를 추가합니다.

- `robim-addon`: RoBIM 패키지
- `pyrevit-extension`: pyRevit 확장
- `autocad-plugin`: AutoCAD 플러그인
- `revit-addin`: Revit 관련 후보. 실제 설치는 아래 지원 패키지 형식이 필요합니다.

앱은 연결 계정이 소유하거나 협업·조직 권한으로 접근할 수 있는 저장소를 페이지 단위로 조회합니다. 위 토픽이 있는 후보의 표시 정보와 구조를 확인한 뒤 보여줍니다. 일반 `python`, `revit`, `autocad` 토픽이나 이름만으로 추측하지 않습니다. 기존 RoBIM-CAD / RoBIM-pyrevit는 호환성을 위해 기본 도구로도 인식합니다.

사용자가 URL로 직접 등록한 저장소는 토픽이 없어도 목록에 유지합니다. 접근할 수 없어진 등록 저장소는 캐시와 경고를 유지하여 로컬 설치 제거·복원이 가능하게 합니다. 자동 검색만 된 저장소는 새 계정의 권한에 맞춰 다시 조회합니다. 인증 실패나 API 한도 오류를 '관련 저장소 없음'으로 처리하지 않고 화면/작업 기록에 표시합니다.

## 패키지 표시 파일

저장소 **기본 브랜치 루트**에 `robim-package.json`을 둡니다. 토픽은 검색 후보를 표시하고 이 파일은 설치 형식을 정의합니다. 파일만 추가하고 토픽이 없으면 URL 직접 등록을 이용하십시오.

```json
{
  "schemaVersion": 1,
  "packages": [
    {
      "id": "my-tools",
      "name": "My Revit Tools",
      "host": "pyrevit",
      "path": ".",
      "folder": "MyTools.extension",
      "distribution": "git",
      "description": "Revit tools"
    }
  ]
}
```

- `id`: 저장소 내 고유한 소문자 영문·숫자·하이픈 ID. 설치 후 변경하지 마십시오.
- `host`: `pyrevit`, `autocad`, 또는 `revit`.
- `path`: Git 사본 또는 ZIP 내부의 패키지 상대 경로. 루트는 `.`이며 절대 경로·상위 경로는 허용하지 않습니다.
- `folder`: 단일 설치 폴더명. `.extension` 또는 `.bundle`로 끝나야 합니다. RoBIM 기본 도구의 예약 폴더명을 다른 저장소에서 사용할 수 없습니다.
- `distribution`: `git` 또는 `release`. Git은 기본 브랜치의 커밋 사본을 설치하며 빌드·설치 스크립트는 실행하지 않습니다.
- `asset`: release 방식에서 내려받을 ZIP 파일명. 릴리스에서 해당 파일명과 일치하는 최신 배포를 찾고 GitHub의 SHA-256 값과 다운로드 해시를 비교합니다. 파일 크기는 128 MiB 이하입니다.

기존 pyRevit 저장소는 루트의 `.tab` 폴더 또는 한 개의 `.extension` 폴더 구조도 인식합니다. AutoCAD는 컴파일된 실행 모듈을 포함하는 `.bundle`이 필요하며 `PackageContents.xml`의 모듈 경로와 실제 파일을 검증합니다. 소스 C# 프로젝트만 있는 저장소를 자동 빌드하지 않습니다. 독립 Revit C#은 `host: "revit"`, `.bundle` 폴더, `distribution: "release"`를 사용합니다. PackageContents.xml의 Revit/Win64/버전 범위, .addin 등록과 상대 DLL 경로를 검사합니다. Revit을 종료한 뒤 설치·업데이트·삭제·복원하며 pyRevit은 필요하지 않습니다. 이 일반 .addin 직접 등록 경로는 실행 중 DLL Reload 경로가 아닙니다. 별도의 `robim-module.json` command-only 모듈은 고정 Host의 Native Reload 경로를 사용하며 [현재 개발자 설명서](developer/README.md)를 참고합니다.

예제: [pyRevit 표시 파일](examples/pyrevit-robim-package.json), [AutoCAD Release 표시 파일 예제](examples/cad-release-package.json).

## 설치와 관리

URL 확인·등록과 자동 조회는 도구를 실행하거나 설치하지 않습니다. 등록 후 Revit / AutoCAD 화면에서 설치 버튼을 누르면 경로와 저장소를 확인한 후 설치합니다. 제품 종료 검사, 기존 외부 폴더 덮어쓰기 차단, 로컬 변경 보존, 파일 해시와 백업·복원은 기존 설치 엔진을 사용합니다. 새 확장은 다음 CAD/Revit 실행 시 원본 도구의 로딩 설정에 따라 실행될 수 있습니다.

RoBIM 기본 두 도구는 Revit / AutoCAD 화면으로 연결하여 기존 외부 설치·브랜치 보존 정책을 유지합니다. 다른 확장은 별도 설치 기록으로 관리합니다. 일반 확장 패키지는 현재 설치·업데이트·제거·복원 시 제품 종료가 필요하며 Preview 3의 Revit 내부 Reload 연결은 RoBIM 기본/개발자 도구에 적용합니다.

관리 파일 `github-auth.json`에는 인증 방식과 ID만, `extension-repositories.json`에는 URL·패키지 정의만 저장합니다. 등록 파일과 다운로드 캐시는 `%LOCALAPPDATA%\RoBIM\Manager`에 있습니다.

참고: [GitHub 저장소 REST API](https://docs.github.com/en/rest/repos/repos), [Windows CredWrite](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew).

독립 시트 생성 예제: https://github.com/gagaggan/RoBIM-Revit-Sheets (`robim-package.json`).
