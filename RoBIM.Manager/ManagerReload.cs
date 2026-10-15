using System.IO;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private void ShowReloadReady() => MessageBox.Show(this,
        "준비 완료.\n\nRevit → RoBIM TEST → RoBIM 연결 TEST → 적용 및 Reload\n\n버튼이 없으면 pyRevit의 Reload를 한 번 누르세요.", Text);

    private async Task PrepareUserReload()
    {
        if (SelectedStatus() is not { } row || row.Tool.Kind != ToolKind.PyRevit)
        { MessageBox.Show(this, "Revit 도구를 선택하십시오. CAD DLL은 재시작 후 설치합니다.", Text); return; }
        if (!Confirm("현재 Revit 도구의 업데이트를 준비합니까?\n기존 Git 설치는 현재 브랜치를 유지합니다. 적용은 Revit 안의 버튼에서 진행합니다.\n" + row.Path)) return;
        await Run("Revit Reload 준비", async () =>
        {
            if (!Directory.Exists(paths.Target(Catalog.ReloadBridge)))
            {
                _ = await EnvironmentProbe.RevitRoots();
                await Task.Run(() => ReloadBridge.Install(paths, Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip")));
            }
            var reload = new RevitReload(paths);
            if (row.External)
            {
                var branch = await ProcessRunner.GitRequired(row.Path, "branch", "--show-current");
                var source = await sources.DownloadRevit(branch);
                await reload.PrepareExternal(row.Path, source);
            }
            else
            {
                if (await EnvironmentProbe.ExternalRevit(paths.Target(row.Tool)) is not null)
                    throw new IOException("기존 설치가 있습니다. 상태를 새로 고쳐 기존 연결을 사용하십시오.");
                var source = await sources.DownloadRevit();
                var output = SafeFiles.Child(paths.Data, "Prepared/" + Guid.NewGuid().ToString("N"));
                var package = await Task.Run(() => RevitPackages.Prepare(source, output, false));
                await Task.Run(() => reload.Prepare(row.Tool, package));
            }
            WriteLog("Revit 업데이트 준비 완료. Revit 안에서 적용 및 Reload를 실행하십시오.");
            ShowReloadReady();
        });
    }
}
