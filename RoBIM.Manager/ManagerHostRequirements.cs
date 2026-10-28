using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private void EnsureHostRuntime(bool cad)
    {
        var root=HostRequirements.HostRoot(cad);
        if (!File.Exists(Path.Combine(root,cad ? "acdbmgd.runtimeconfig.json" : "RevitNET.runtimeconfig.json")))
        {
            using var picker = new FolderBrowserDialog { Description = (cad ? "AutoCAD" : "Revit") + " 실행 파일이 있는 설치 폴더를 선택하세요.", UseDescriptionForTitle=true };
            if (picker.ShowDialog(this)!=DialogResult.OK) throw new IOException("설치 폴더 선택이 취소되었습니다.");
            HostRequirements.SaveLocation(cad,picker.SelectedPath);
        }
        HostRequirements.Ensure(cad);
    }
}
