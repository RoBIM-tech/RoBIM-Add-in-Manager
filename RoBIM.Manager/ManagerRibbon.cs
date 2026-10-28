using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel ribbonPage = new() { Dock = DockStyle.Fill };
    private RibbonEditorControl? ribbonEditor;
    private void BuildRibbonPage()
    {
        ribbonEditor = new(new RibbonLayouts(paths), preview);
        ribbonPage.Controls.Add(ribbonEditor);
        var connection = MakeButton("Revit 연결 설치 / 업데이트");
        connection.AutoSize = true;
        connection.AccessibleName = "Revit 리본 연결 설치 및 업데이트";
        var connectionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        connectionBar.Controls.Add(connection);
        connectionBar.Controls.Add(Note("리본 편집이 적용되지 않으면 Revit을 저장하고 종료한 뒤 연결을 업데이트하세요."));
        ribbonPage.Controls.Add(connectionBar);
        connection.Click += async (_, _) =>
        {
            if (preview) return;
            await Run("Revit 리본 연결 업데이트", () =>
            {
                EnvironmentProbe.EnsureHostClosed(Catalog.RevitHost);
                InstallRibbonHost();
                WriteLog("Revit 연결 업데이트 완료 · 저장된 리본 배치는 다음 Revit 시작 시 적용됩니다.");
                return Task.CompletedTask;
            }, requiresAccess: false);
        };
        ribbonEditor.RefreshRequested = async () => { if (!preview) await Run("리본 목록 확인", RefreshStatuses); RefreshRibbonEditor(); };
        ribbonEditor.ApplyRequested = async layout =>
        {
            await Task.Yield();
            if (RevitRuntimePolicy.NativeOnly(paths))
            {
                if (!ReloadBridge.RevitRunning()) InstallRibbonHost();
                else if (!NativeHostPackage.Matches(paths, RibbonHostArchive))
                    return "배치 저장 완료 · Revit 연결 업데이트가 필요합니다. Revit을 저장하고 종료한 뒤 위의 ‘Revit 연결 설치 / 업데이트’를 누르세요. 현재 Revit에는 아직 적용되지 않았습니다.";
            }
            return RequestAutomaticRevitApply(native: true, pyRevit: new RevitReload(paths).PendingSelection() is not null, layout: true);
        };
    }
    private string RibbonHostArchive => Path.Combine(AppContext.BaseDirectory, "RoBIM-Host-Revit2026.zip");
    private void InstallRibbonHost()
    {
        EnsureHostRuntime(cad: false);
        NativeCommands().InstallHost(RibbonHostArchive,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026"));
    }
    private void RefreshRibbonEditor()
    {
        var tools = RibbonLayouts.WithManagementTools(CommandInventory().Where(x => x.Kind is ButtonKind.PyRevit or ButtonKind.Native)
            .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon)));
        ribbonEditor?.LoadTools(tools);
    }
}
