namespace RoBIM.Manager.Core;

public enum InstallAction { Connect, WaitForHost, Install, BundledCad, PrepareRevit, ApplyInRevit, Blocked }
public sealed record InstallStep(InstallAction Action, string Button, string Instruction, bool Enabled = true);

public static class InstallFlow
{
    public static InstallStep Decide(ToolStatus status, bool hostRunning, bool authenticated, bool pending)
    {
        var name = status.Tool.Kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit";
        var builtin = status.Tool.Id == Catalog.Cad.Id || status.Tool.Id == Catalog.Revit.Id;
        if (status.Modified) return new(InstallAction.Blocked, "설치 상태 확인 필요", "기존 파일에 변경이나 중복 설치가 있습니다. 추가 작업에서 설치 위치를 확인하십시오.", false);
        if (status.External && status.Tool.Kind == ToolKind.AutoCAD)
            return new(InstallAction.Blocked, "기존 설치 확인 필요", "다른 곳에서 설치한 CAD 도구입니다. 설치 폴더를 확인하십시오.", false);
        if (pending && builtin && status.Tool.Kind == ToolKind.PyRevit && hostRunning)
            return new(InstallAction.ApplyInRevit, "Revit에서 적용하기", "준비 완료 → Revit의 ‘적용 및 Reload’ 버튼을 누르세요.");
        if (hostRunning && (status.Tool.Kind == ToolKind.AutoCAD || !builtin || (!status.Managed && !status.External)))
            return new(InstallAction.WaitForHost, name + " 종료 후 설치", name + "에서 작업을 저장하고 종료하세요. 돌아오면 설치 버튼이 켜집니다.", false);
        if (!authenticated && builtin && status.Tool.Kind == ToolKind.AutoCAD && !status.Managed)
            return new(InstallAction.BundledCad, "설치하기", "AutoCAD 종료 후 연결 모듈을 설치합니다. 완료 후 이 화면에서 버튼을 선택하고 CAD Reload를 누르세요.");
        if (!authenticated) return new(InstallAction.Connect, "GitHub 연결하기", "처음 한 번 GitHub 계정을 연결하면 설치할 수 있습니다.");
        if (hostRunning)
            return new(InstallAction.PrepareRevit, "업데이트 준비", "Revit을 켜 둔 채 준비합니다. 완료 후 Revit에서 ‘적용 및 Reload’를 누르세요.");
        return new(InstallAction.Install, status.Managed || status.External ? "업데이트하기" : "설치하기",
            "버튼을 눌러 설치하세요. 완료 후 " + name + "을 실행" + (status.Tool.Kind == ToolKind.AutoCAD && builtin ? "하고 Manager에서 버튼 선택 → CAD Reload로 적용하세요." : "하여 도구를 확인하세요."));
    }
}
